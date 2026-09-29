using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class TestLauncher
{
    private const string ConnectionVariable = "DAMENG_TEST_CONNECTION_STRING";
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

    public static int Run(string[] args, string root, JsonObject? secrets)
    {
        if (args.Length < 1 || args.Length > 3 || args.Length == 2
            || (args.Length == 3 && args[1] != "--filter")
            || (args.Length == 3 && string.IsNullOrWhiteSpace(args[2])))
        {
            throw new ArgumentException("Invalid test arguments.");
        }

        var selected = SelectLanes(args[0]);
        var customFilter = args.Length == 3 ? args[2] : null;
        var adminConnection = secrets?["AdminConnectionString"]?.GetValue<string>();
        var savedTestConnection = secrets?["ConnectionString"]?.GetValue<string>();
        var overrideConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
        var testConnection = !string.IsNullOrWhiteSpace(overrideConnection)
            ? overrideConnection : savedTestConnection;

        if (selected.Any(lane => lane.NeedsAdmin) && string.IsNullOrWhiteSpace(adminConnection))
        {
            throw new InvalidOperationException("Admin test connection unavailable.");
        }
        if (selected.Any(lane => lane.NeedsTestConnection)
            && string.IsNullOrWhiteSpace(testConnection))
        {
            throw new InvalidOperationException("Test connection unavailable.");
        }
        if (selected.Any(lane => lane.NeedsTestConnection)
            && string.IsNullOrWhiteSpace(overrideConnection)
            && secrets?["ProvisioningStatus"]?.GetValue<string>() != "ready")
        {
            throw new InvalidOperationException("Persistent test schema is not ready.");
        }

        var redact = CreateRedactor(adminConnection, savedTestConnection, overrideConnection);
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
            Console.WriteLine("Running " + lane.Name + " tests.");
            var project = Path.Combine(root, lane.Project);
            var restoreArgs = new[]
            {
                "restore", project, "--locked-mode", "--disable-parallel", "-m:1",
                "/nodeReuse:false", "/p:UseSharedCompilation=false"
            };
            var restoreCode = RunProcess(dotnetHost, restoreArgs, root, cliHome, null, redact);
            if (restoreCode != 0)
            {
                Console.Error.WriteLine(lane.Name + ": restore failed; this lane is not accepted.");
                if (overallCode == 0) overallCode = restoreCode;
                continue;
            }

            var testArgs = new List<string>
            {
                "test", project, "--no-restore", "-m:1", "/nodeReuse:false",
                "/p:UseSharedCompilation=false", "--disable-build-servers"
            };
            testArgs.Add("--logger");
            testArgs.Add("trx;LogFileName=" + lane.Id + ".trx");
            testArgs.Add("--results-directory");
            testArgs.Add(resultDirectory);
            var filter = CombineFilters(lane.RequiredFilter, customFilter);
            if (filter is not null)
            {
                testArgs.Add("--filter");
                testArgs.Add(filter);
            }
            var connection = lane.NeedsAdmin ? adminConnection
                : lane.NeedsTestConnection ? testConnection : null;
            var code = RunProcess(dotnetHost, testArgs, root, cliHome, connection, redact);
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

    private static IReadOnlyList<Lane> SelectLanes(string name)
    {
        const string functional = "test/W.EntityFrameworkCore.Dameng.FunctionalTests/W.EntityFrameworkCore.Dameng.FunctionalTests.csproj";
        const string specification = "test/W.EntityFrameworkCore.Dameng.Specification.Tests/W.EntityFrameworkCore.Dameng.Specification.Tests.csproj";
        const string unit = "test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj";
        var normalLane = new Lane("functional", "functional", functional, NormalFunctionalFilter, false, true);
        var specLane = new Lane("specification", "specification", specification, null, false, true);
        var adminLane = new Lane("admin", "admin migration scripts", functional, AdminFilter, true, false);
        var probeLane = new Lane("probes", "capability probes", functional, ProbeFilter, false, true);
        var unitLane = new Lane("unit", "unit", unit, null, false, false);
        return name switch
        {
            "functional" => [normalLane],
            "specification" => [specLane],
            "admin" => [adminLane],
            "probes" => [probeLane],
            "unit" => [unitLane],
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
        string cliHome, string? connection, Func<string, string> redact)
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
        if (connection is null) start.Environment.Remove(ConnectionVariable);
        else start.Environment[ConnectionVariable] = connection;

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
