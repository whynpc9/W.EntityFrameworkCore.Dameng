using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class TestLauncher
{
    private const string ConnectionVariable = "DAMENG_TEST_CONNECTION_STRING";
    private const string WTransportSecurityVariable = "DAMENG_TEST_W_TRANSPORT_SECURITY";
    private const string ScriptClass = "DamengMigrationScriptFunctionalTests";
    private const string NormalFunctionalFilter =
        "FullyQualifiedName!~" + ScriptClass + "&Category!~CapabilityProbe";
    private const string AdminFilter = "FullyQualifiedName~" + ScriptClass;
    private const string ProbeFilter =
        "FullyQualifiedName!~" + ScriptClass + "&Category=CapabilityProbe";

    private sealed record Lane(string Id, string Name, string Project, string? RequiredFilter, bool NeedsAdmin,
        bool NeedsTestConnection);

    private sealed record TrxCounts(int Total, int Executed, int Passed, int Failed, int Skipped,
        string Outcome);

    public static int Run(string[] args, string root, Func<JsonObject?> loadSecrets)
    {
        var options = ParseOptions(args);
        var configuration = options.GetValueOrDefault("--configuration", "Debug");
        var driver = options.GetValueOrDefault("--driver", "official");
        if (driver is not ("official" or "w"))
        {
            throw new ArgumentException("Unknown driver; use official or w.");
        }

        // The W transport mode is non-secret configuration; it only reaches W test processes.
        var wTransportSecurity = options.GetValueOrDefault("--w-transport-security")
            ?? Environment.GetEnvironmentVariable(WTransportSecurityVariable);
        if (wTransportSecurity is not (null or "RequireTls" or "PlaintextAllowed"))
        {
            throw new ArgumentException("Unknown W transport security; use RequireTls or PlaintextAllowed.");
        }
        if (driver != "w" && options.ContainsKey("--w-transport-security"))
        {
            throw new ArgumentException("--w-transport-security applies only to --driver w.");
        }

        var selected = SelectLanes(args[0], driver);
        var customFilter = options.GetValueOrDefault("--filter");
        var laneEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [WTransportSecurityVariable] = driver == "w" ? wTransportSecurity : null
        };
        var connections = LoadConnections(selected, loadSecrets,
            () => Environment.GetEnvironmentVariable(ConnectionVariable));
        var adminConnection = connections.Admin;
        var testConnection = connections.Test;
        var redact = connections.Redact;
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Environment.ProcessPath ?? "dotnet";
        var cliHome = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME");
        if (string.IsNullOrWhiteSpace(cliHome))
        {
            cliHome = Path.Combine(Path.GetTempPath(), "dameng-local-test-cli");
            Directory.CreateDirectory(cliHome);
        }

        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var resultDirectory = Path.Combine(root, "artifacts", "query-translation", "local-test", runId);
        Directory.CreateDirectory(resultDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(resultDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var overallCode = 0;

        foreach (var lane in selected)
        {
            Console.WriteLine("Running " + lane.Name + " tests (" + configuration + ").");
            var project = Path.Combine(root, lane.Project);
            var restoreArgs = new[]
            {
                "restore", project, "--locked-mode", "--disable-parallel", "-m:1",
                "/nodeReuse:false", "/p:UseSharedCompilation=false"
            };
            var restoreCode = RunProcess(dotnetHost, restoreArgs, root, cliHome, null, laneEnvironment, redact);
            if (restoreCode != 0)
            {
                Console.Error.WriteLine(lane.Name + ": restore failed; this lane is not accepted.");
                if (overallCode == 0) overallCode = restoreCode;
                continue;
            }

            laneEnvironment["DAMENG_TEST_RECOVERY_DIRECTORY"] = lane.NeedsAdmin ? Path.Combine(resultDirectory, "recovery") : null;
            var testArgs = BuildTestArguments(lane, project, configuration, resultDirectory, customFilter);
            var connection = lane.NeedsAdmin ? adminConnection
                : lane.NeedsTestConnection ? testConnection : null;
            var code = RunProcess(dotnetHost, testArgs, root, cliHome, connection, laneEnvironment, redact);
            var trxPath = Path.Combine(resultDirectory, lane.Id + ".trx");
            var counts = ReadAndRedactTrx(trxPath, redact);
            if (counts is null)
            {
                Console.Error.WriteLine(lane.Name + ": TRX missing or unreadable; this lane is not accepted.");
                if (overallCode == 0) overallCode = code != 0 ? code : 3;
                continue;
            }

            Console.WriteLine(lane.Name + ": outcome=" + counts.Outcome
                + ", total=" + counts.Total.ToString(CultureInfo.InvariantCulture)
                + ", executed=" + counts.Executed.ToString(CultureInfo.InvariantCulture)
                + ", passed=" + counts.Passed.ToString(CultureInfo.InvariantCulture)
                + ", failed=" + counts.Failed.ToString(CultureInfo.InvariantCulture)
                + ", skipped=" + counts.Skipped.ToString(CultureInfo.InvariantCulture)
                + "; TRX: " + trxPath);
            if (!IsAccepted(code, counts))
            {
                Console.Error.WriteLine(lane.Name
                    + ": incomplete run, nonzero exit, zero tests, failure, or skip; this lane is not accepted.");
                if (overallCode == 0) overallCode = code != 0 ? code : 3;
            }
        }

        return overallCode;
    }

    private sealed record Connections(string? Admin, string? Test, Func<string, string> Redact);

    private static Connections LoadConnections(IReadOnlyList<Lane> selected, Func<JsonObject?> loadSecrets,
        Func<string?> loadOverride)
    {
        if (!selected.Any(lane => lane.NeedsAdmin || lane.NeedsTestConnection))
            return new Connections(null, null, text => text);
        var secrets = loadSecrets();
        var admin = secrets?["AdminConnectionString"]?.GetValue<string>();
        var saved = secrets?["ConnectionString"]?.GetValue<string>();
        var overrideConnection = selected.Any(lane => lane.NeedsTestConnection) ? loadOverride() : null;
        var test = !string.IsNullOrWhiteSpace(overrideConnection) ? overrideConnection : saved;
        if (selected.Any(lane => lane.NeedsAdmin) && string.IsNullOrWhiteSpace(admin))
            throw new InvalidOperationException("Admin test connection unavailable.");
        if (selected.Any(lane => lane.NeedsTestConnection) && string.IsNullOrWhiteSpace(test))
            throw new InvalidOperationException("Test connection unavailable.");
        if (selected.Any(lane => lane.NeedsTestConnection) && string.IsNullOrWhiteSpace(overrideConnection)
            && secrets?["ProvisioningStatus"]?.GetValue<string>() != "ready")
            throw new InvalidOperationException("Persistent test schema is not ready.");
        return new Connections(admin, test, CreateRedactor(admin, saved, overrideConnection));
    }

    private static List<string> BuildTestArguments(Lane lane, string project, string configuration,
        string resultDirectory, string? customFilter)
    {
        var arguments = new List<string>
        {
            "test", project, "--configuration", configuration, "--no-restore", "-m:1", "/nodeReuse:false",
            "/p:UseSharedCompilation=false", "--disable-build-servers", "--logger", "trx;LogFileName=" + lane.Id + ".trx",
            "--results-directory", resultDirectory
        };
        var filter = CombineFilters(lane.RequiredFilter, customFilter);
        if (filter is not null) { arguments.Add("--filter"); arguments.Add(filter); }
        return arguments;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        if (args.Length < 1 || args.Length % 2 == 0)
        {
            throw new ArgumentException("Invalid test arguments.");
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (args[index] is not ("--filter" or "--driver" or "--w-transport-security" or "--configuration")
                || string.IsNullOrWhiteSpace(args[index + 1])
                || !options.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Invalid test arguments.");
            }
        }

        var configuration = options.GetValueOrDefault("--configuration", "Debug");
        if (configuration is not ("Debug" or "Release"))
            throw new ArgumentException("Unknown configuration; use Debug or Release.");
        return options;
    }

    private static IReadOnlyList<Lane> SelectLanes(string name, string driver)
    {
        // W lanes run the same shared sources through the W.DamengProvider test projects and
        // write separate TRX files, so the two drivers' results can never be merged.
        var w = driver == "w";
        var prefix = w ? "test/W.EntityFrameworkCore.Dameng.WDriver." : "test/W.EntityFrameworkCore.Dameng.";
        var suffix = w ? "-w" : "";
        var label = w ? "W " : "";
        var functional = prefix + "FunctionalTests/" + prefix[5..] + "FunctionalTests.csproj";
        var specification = prefix + "Specification.Tests/" + prefix[5..] + "Specification.Tests.csproj";
        var unit = prefix + "Tests/" + prefix[5..] + "Tests.csproj";
        var normalLane = new Lane("functional" + suffix, label + "functional", functional, NormalFunctionalFilter, false, true);
        var specLane = new Lane("specification" + suffix, label + "specification", specification, null, false, true);
        var adminLane = new Lane("admin" + suffix, label + "admin migration scripts", functional, AdminFilter, true, false);
        var probeLane = new Lane("probes" + suffix, label + "capability probes", functional, ProbeFilter, false, true);
        var offlineLane = new Lane("offline-fixtures" + suffix, label + "offline fixtures", functional, "Category=OfflineFixture", false, false);
        var unitLane = new Lane("unit" + suffix, label + "unit", unit, null, false, false);
        return name switch
        {
            "functional" => [normalLane],
            "specification" => [specLane],
            "admin" => [adminLane],
            "probes" => [probeLane],
            "unit" => [unitLane],
            "offline-fixtures" => [offlineLane],
            "all" => [unitLane, normalLane, specLane, adminLane],
            _ => throw new ArgumentException("Unknown test lane.")
        };
    }

    private static string? CombineFilters(string? required, string? custom)
        => (required, custom) switch
        {
            (null, null) => null,
            (not null, null) => required,
            (null, not null) => custom,
            _ => "(" + required + ")&(" + custom + ")"
        };

    private static int RunProcess(string dotnetHost, IEnumerable<string> arguments, string root,
        string cliHome, string? connection, IReadOnlyDictionary<string, string?> environment,
        Func<string, string> redact)
    {
        var start = BuildProcessStartInfo(dotnetHost, arguments, root, cliHome, connection, environment);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Unable to start dotnet.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        var output = redact(outputTask.Result);
        var error = redact(errorTask.Result);
        if (output.Length > 0) Console.Write(output);
        if (error.Length > 0) Console.Error.Write(error);
        return process.ExitCode;
    }

    private static ProcessStartInfo BuildProcessStartInfo(string dotnetHost, IEnumerable<string> arguments, string root,
        string cliHome, string? connection, IReadOnlyDictionary<string, string?> environment)
    {
        var start = new ProcessStartInfo(dotnetHost)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_HOME"] = cliHome;
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment.Remove("ConnectionStrings__DamengCliFixture");
        if (connection is null) start.Environment.Remove(ConnectionVariable);
        else start.Environment[ConnectionVariable] = connection;
        foreach (var (name, value) in environment)
        {
            if (value is null) start.Environment.Remove(name);
            else start.Environment[name] = value;
        }

        return start;
    }

    private static TrxCounts? ReadAndRedactTrx(string path, Func<string, string> redact)
    {
        if (!File.Exists(path)) return null;
        var tempPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            var document = XDocument.Load(path);
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    if (!attribute.IsNamespaceDeclaration) attribute.Value = redact(attribute.Value);
                }
            }
            foreach (var node in document.DescendantNodes().OfType<XText>())
            {
                node.Value = redact(node.Value);
            }
            foreach (var comment in document.DescendantNodes().OfType<XComment>())
            {
                comment.Value = redact(comment.Value);
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(tempPath, options))
            {
                document.Save(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);

            var saved = XDocument.Load(path);
            var summary = saved.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "ResultSummary");
            var counters = summary?.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Counters");
            if (counters is null) return null;
            var total = AttributeInt(counters, "total");
            var executed = AttributeInt(counters, "executed");
            var passed = AttributeInt(counters, "passed");
            var failed = AttributeInt(counters, "failed") + AttributeInt(counters, "error")
                + AttributeInt(counters, "timeout") + AttributeInt(counters, "aborted");
            var skipped = AttributeInt(counters, "notExecuted")
                + AttributeInt(counters, "inconclusive") + AttributeInt(counters, "notRunnable");
            skipped = Math.Max(skipped, Math.Max(0, total - executed));
            var outcome = summary?.Attribute("outcome")?.Value ?? "missing";
            return new TrxCounts(total, executed, passed, failed, skipped, outcome);
        }
        catch
        {
            // Preserve a safe diagnostic artifact even when the TRX is malformed.
            try
            {
                File.WriteAllText(path, redact(File.ReadAllText(path)));
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch
            {
                // The containing run directory remains private to the current user.
            }
            return null;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static bool IsAccepted(int exitCode, TrxCounts counts)
        => exitCode == 0 && counts.Outcome == "Completed" && counts.Total > 0
            && counts.Total == counts.Executed && counts.Passed == counts.Executed
            && counts.Failed == 0 && counts.Skipped == 0;

    internal static int SelfTest()
    {
        foreach (var driver in new[] { "official", "w" })
        {
            foreach (var name in new[] { "unit", "offline-fixtures" })
            {
                var lanes = SelectLanes(name, driver);
                var connections = LoadConnections(lanes,
                    () => throw new InvalidOperationException("Offline secrets loader was called."),
                    () => throw new InvalidOperationException("Offline connection override was read."));
                if (connections.Admin is not null || connections.Test is not null || connections.Redact("fixture") != "fixture")
                    throw new InvalidOperationException("Offline connection self-test failed.");
                var lane = lanes.Single();
                if ((driver == "w") != lane.Project.Contains("WDriver", StringComparison.Ordinal))
                    throw new InvalidOperationException("Offline driver project self-test failed.");
                if (name == "offline-fixtures")
                {
                    var filter = CombineFilters(lane.RequiredFilter, "FullyQualifiedName~Fixture");
                    if (filter != "(Category=OfflineFixture)&(FullyQualifiedName~Fixture)")
                        throw new InvalidOperationException("Offline filter self-test failed.");
                }
                var start = BuildProcessStartInfo("dotnet", ["test"], ".", "cli-home", null, new Dictionary<string, string?>());
                if (start.Environment.ContainsKey(ConnectionVariable) || start.Environment.ContainsKey("ConnectionStrings__DamengCliFixture"))
                    throw new InvalidOperationException("Offline process environment self-test failed.");
            }
        }
        foreach (var name in new[] { "functional", "specification", "admin", "probes", "all" })
        {
            var calls = 0;
            var lanes = SelectLanes(name, "official");
            try { LoadConnections(lanes, () => { calls++; return null; }, () => null); }
            catch (InvalidOperationException) { if (calls == 1) continue; }
            throw new InvalidOperationException("Database lane did not require configuration.");
        }
        var synthetic = new JsonObject
        {
            ["AdminConnectionString"] = "Server=admin.invalid;User ID=synthetic;Password=admin-only",
            ["ConnectionString"] = "Server=test.invalid;User ID=synthetic;Password=test-only",
            ["ProvisioningStatus"] = "ready"
        };
        var normal = LoadConnections(SelectLanes("functional", "official"), () => synthetic, () => null);
        if (normal.Test != synthetic["ConnectionString"]!.GetValue<string>())
            throw new InvalidOperationException("Ordinary connection selection changed.");
        var adminOnly = LoadConnections(SelectLanes("admin", "official"), () => synthetic,
            () => throw new InvalidOperationException("Admin must not read the ordinary override."));
        if (adminOnly.Admin != synthetic["AdminConnectionString"]!.GetValue<string>())
            throw new InvalidOperationException("Admin connection selection changed.");
        synthetic["ProvisioningStatus"] = "pending";
        try
        {
            LoadConnections(SelectLanes("functional", "official"), () => synthetic, () => null);
            throw new ArgumentException("Unready schema was accepted.");
        }
        catch (InvalidOperationException) { }
        const string syntheticOverride = "Server=override.invalid;User ID=synthetic;Password=override-only";
        if (LoadConnections(SelectLanes("functional", "official"), () => synthetic, () => syntheticOverride).Test != syntheticOverride)
            throw new InvalidOperationException("Ordinary override selection changed.");
        try { SelectLanes("unknown", "official"); throw new InvalidOperationException("Unknown lane was accepted."); }
        catch (ArgumentException) { }
        if (SelectLanes("all", "official").Any(lane => lane.Id.StartsWith("offline", StringComparison.Ordinal)))
            throw new InvalidOperationException("All lane semantics changed.");
        Console.WriteLine("Offline lane isolation self-test passed.");
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var parsed = ParseOptions(["unit", "--configuration", configuration]);
            var arguments = BuildTestArguments(SelectLanes("unit", "official")[0], "project", parsed["--configuration"], "results", null);
            if (arguments[arguments.IndexOf("--configuration") + 1] != configuration)
                throw new InvalidOperationException("Configuration argument self-test failed.");
        }
        if (ParseOptions(["unit"]).GetValueOrDefault("--configuration", "Debug") != "Debug")
            throw new InvalidOperationException("Default configuration self-test failed.");
        foreach (var invalid in new[] {
            new[] { "unit", "--configuration", "" }, new[] { "unit", "--configuration", "release" },
            new[] { "unit", "--configuration", "Arbitrary" }, new[] { "unit", "--configuration", "Debug", "--configuration", "Release" },
            new[] { "unit", "--configuration" } })
        {
            try { ParseOptions(invalid); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid configuration was accepted.");
        }
        Console.WriteLine("Configuration self-test passed.");
        const string fakeConnection = "Data Source=unit-host.invalid;User ID=SA;Password=sample-only-secret";
        const string sample = """
            <TestRun xmlns="urn:synthetic-trx">
              <Results>
                <UnitTestResult outcome="Passed">
                  <Output><ErrorInfo>
                    <Message>SA at unit-host.invalid</Message>
                    <StackTrace>Password=sample-only-secret</StackTrace>
                  </ErrorInfo></Output>
                </UnitTestResult>
              </Results>
              <Diagnostics>Error Message: Message; UID=SA; 'SA'; standalone SA; Massive</Diagnostics>
              <ResultSummary outcome="Completed">
                <Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" />
              </ResultSummary>
            </TestRun>
            """;
        var directory = Path.Combine(Path.GetTempPath(), "dameng-trx-selftest-"
            + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sample.trx");
            File.WriteAllText(path, sample);
            var redact = CreateRedactor(fakeConnection);
            var counts = ReadAndRedactTrx(path, redact);
            var saved = XDocument.Load(path);
            var message = saved.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message");
            var stack = saved.Descendants().FirstOrDefault(e => e.Name.LocalName == "StackTrace");
            var diagnostics = saved.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Diagnostics")?.Value;
            var savedText = File.ReadAllText(path);
            if (counts is null || !IsAccepted(0, counts) || message is null || stack is null
                || !message.Value.Contains("[redacted]", StringComparison.Ordinal)
                || !stack.Value.Contains("[redacted]", StringComparison.Ordinal)
                || diagnostics is null
                || !diagnostics.Contains("Error Message: Message", StringComparison.Ordinal)
                || !diagnostics.Contains("UID=[redacted]", StringComparison.Ordinal)
                || !diagnostics.Contains("'[redacted]'", StringComparison.Ordinal)
                || !diagnostics.Contains("standalone [redacted]", StringComparison.Ordinal)
                || !diagnostics.Contains("Massive", StringComparison.Ordinal)
                || savedText.Contains("unit-host.invalid", StringComparison.OrdinalIgnoreCase)
                || savedText.Contains("sample-only-secret", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Synthetic TRX redaction failed.");
            }

            var summary = saved.Descendants().First(e => e.Name.LocalName == "ResultSummary");
            summary.SetAttributeValue("outcome", "Aborted");
            saved.Save(path);
            var aborted = ReadAndRedactTrx(path, redact);
            if (aborted is null || IsAccepted(0, aborted))
            {
                throw new InvalidOperationException("Synthetic TRX aborted-run check failed.");
            }

            var partial = XDocument.Load(path);
            var partialSummary = partial.Descendants()
                .First(e => e.Name.LocalName == "ResultSummary");
            partialSummary.SetAttributeValue("outcome", "Completed");
            var partialCounters = partialSummary.Descendants()
                .First(e => e.Name.LocalName == "Counters");
            partialCounters.SetAttributeValue("total", "2");
            partialCounters.SetAttributeValue("executed", "2");
            partialCounters.SetAttributeValue("passed", "1");
            partial.Save(path);
            var incomplete = ReadAndRedactTrx(path, redact);
            if (incomplete is null || IsAccepted(0, incomplete))
            {
                throw new InvalidOperationException("Synthetic TRX incomplete-count check failed.");
            }

            Console.WriteLine("Synthetic TRX self-test passed.");
            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int AttributeInt(XElement element, string name)
        => int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result) ? result : 0;

    internal static Func<string, string> CreateRedactor(params string?[] connectionStrings)
    {
        var literalTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connectionString in connectionStrings)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) continue;
            literalTokens.Add(connectionString);
            try
            {
                var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
                foreach (string itemKey in builder.Keys)
                {
                    var key = Regex.Replace(itemKey, "[\\s_]", "", RegexOptions.CultureInvariant);
                    var value = Convert.ToString(builder[itemKey], CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(value)) continue;
                    if (key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("Server", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
                        literalTokens.Add(value);
                        var host = value.Split(':', '/')[0];
                        if (host.Length > 0) literalTokens.Add(host);
                    }
                    else if (key.Equals("UID", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("UserID", StringComparison.OrdinalIgnoreCase))
                    {
                        userTokens.Add(value);
                    }
                    else if (key.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("Password", StringComparison.OrdinalIgnoreCase))
                    {
                        literalTokens.Add(value);
                    }
                }
            }
            catch (ArgumentException)
            {
                // The full string remains redacted even if a driver-specific key cannot be parsed.
            }
        }

        var literalPatterns = literalTokens.OrderByDescending(token => token.Length)
            .Select(Regex.Escape).ToArray();
        var userPatterns = userTokens.OrderByDescending(token => token.Length)
            .Select(Regex.Escape).ToArray();
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        var literalExpression = literalPatterns.Length == 0 ? null
            : new Regex(string.Join("|", literalPatterns), options);
        var userExpression = userPatterns.Length == 0 ? null
            : new Regex("(?<![\\p{L}\\p{N}_])(?:" + string.Join("|", userPatterns)
                + ")(?![\\p{L}\\p{N}_])", options);
        return value =>
        {
            if (literalExpression is not null) value = literalExpression.Replace(value, "[redacted]");
            if (userExpression is not null) value = userExpression.Replace(value, "[redacted]");
            return value;
        };
    }
}
