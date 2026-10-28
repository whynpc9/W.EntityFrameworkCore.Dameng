using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

// Shared verbatim with the offline unit project. No driver-specific ADO.NET dependency.
internal static class DamengCliTestHarness
{
    internal const string ConnectionVariable = "ConnectionStrings__DamengCliFixture";
    internal const string ConnectionAlias = "Name=ConnectionStrings:DamengCliFixture";

    // Raw output stays private: semantic checks must never receive redacted data or dump raw arguments.
    internal sealed class ProcessResult(int exitCode, string rawOutput, string? secret = null)
    {
        internal int ExitCode { get; } = exitCode;
        internal string SafeOutput { get; } = Redact(secret, rawOutput);

        internal void RequireExpectedOutput(string expected)
        {
            if (!rawOutput.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException("CLI output did not contain the expected assembly identity.");
        }

        internal bool HasExpectedToolVersion(string expectedVersion)
            => string.Equals(rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && char.IsDigit(line[0])),
                expectedVersion, StringComparison.Ordinal);

        public override string ToString() => "CLI process result (exit=" + ExitCode + ").";
    }

    internal sealed record AssemblyIdentity(string Name, string Location, Guid Mvid, string Sha256);

    internal static AssemblyIdentity CaptureAssembly(Assembly assembly, string expectedName)
    {
        if (assembly.GetName().Name != expectedName)
            throw new InvalidOperationException("CLI loaded assembly name mismatch.");
        var identity = CaptureFile(assembly.Location, expectedName);
        if (identity.Mvid != assembly.ManifestModule.ModuleVersionId)
            throw new InvalidOperationException("CLI loaded assembly MVID mismatch.");
        return identity;
    }

    internal static AssemblyIdentity CaptureFile(string location, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
            throw new InvalidOperationException("CLI provider assembly location missing.");
        if (AssemblyName.GetAssemblyName(location).Name != expectedName)
            throw new InvalidOperationException("CLI provider file name mismatch.");
        using var stream = File.OpenRead(location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return new AssemblyIdentity(expectedName, Path.GetFullPath(location),
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(location))));
    }

    internal static void VerifyIdentity(AssemblyIdentity expected, AssemblyIdentity actual)
    {
        if (expected.Name != actual.Name || expected.Mvid != actual.Mvid || expected.Sha256 != actual.Sha256)
            throw new InvalidOperationException("CLI provider assembly identity mismatch.");
    }

    internal static void VerifyNestedCopies(IEnumerable<AssemblyIdentity> identities, string outputDirectory)
    {
        foreach (var identity in identities)
            VerifyIdentity(identity, CaptureFile(Path.Combine(outputDirectory, identity.Name + ".dll"), identity.Name));
    }

    internal static string IdentityVerificationSource(IEnumerable<AssemblyIdentity> identities)
        => string.Join("\n", identities.Select(identity => $$"""
            {
                var assembly = System.Reflection.Assembly.Load("{{identity.Name}}");
                var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location)));
                if (assembly.GetName().Name != "{{identity.Name}}" || assembly.ManifestModule.ModuleVersionId != Guid.Parse("{{identity.Mvid}}")
                    || hash != "{{identity.Sha256}}") throw new InvalidOperationException("CLI provider runtime identity mismatch.");
                Console.WriteLine("Provider identity verified: {{identity.Name}} MVID={{identity.Mvid}} SHA256={{identity.Sha256}}");
            }
            """));

    internal static async Task<string> EnsureToolAsync(string dotnet, string repositoryRoot, string lockedVersion,
        Func<ProcessStartInfo, Task<ProcessResult>>? execute = null)
    {
        var run = execute ?? (start => RunAsync(start));
        var directory = Path.Combine(repositoryRoot, "artifacts", "dotnet-ef-tool");
        var tool = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet-ef.exe" : "dotnet-ef");
        async Task<bool> HasLockedVersionAsync()
        {
            if (!File.Exists(tool)) return false;
            try
            {
                var result = await run(CreateProcess(tool, directory, ["--version"], null));
                return result.ExitCode == 0 && result.HasExpectedToolVersion(lockedVersion);
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                return false;
            }
        }
        if (await HasLockedVersionAsync()) return tool;
        Directory.CreateDirectory(directory);
        var action = File.Exists(tool) ? "update" : "install";
        var install = await run(CreateProcess(dotnet, repositoryRoot,
            ["tool", action, "dotnet-ef", "--version", lockedVersion, "--tool-path", directory], null));
        if (install.ExitCode != 0 || !await HasLockedVersionAsync())
            throw new InvalidOperationException("CLI fixture could not establish the locked dotnet-ef version.");
        return tool;
    }

    internal static string CreatePrivateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dameng-cli-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    internal static ProcessStartInfo CreateProcess(string executable, string directory,
        IEnumerable<string> arguments, string? connection)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("DAMENG_TEST_CONNECTION_STRING");
        start.Environment.Remove(ConnectionVariable);
        if (connection is not null) start.Environment[ConnectionVariable] = connection;
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        return start;
    }

    internal static async Task<ProcessResult> RunAsync(ProcessStartInfo start, string? secret = null)
    {
        string? ownedCliHome = null;
        if (!start.Environment.TryGetValue("DOTNET_CLI_HOME", out var cliHome) || string.IsNullOrWhiteSpace(cliHome))
        {
            ownedCliHome = CreatePrivateDirectory();
            start.Environment["DOTNET_CLI_HOME"] = ownedCliHome;
        }
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI process did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                throw new TimeoutException("CLI fixture step timed out.");
            }
            var text = await stdout + await stderr;
            return new ProcessResult(process.ExitCode, text, secret);
        }
        finally
        {
            if (ownedCliHome is not null) Directory.Delete(ownedCliHome, recursive: true);
        }
    }

    internal static string Redact(string? secret, string text)
    {
        if (string.IsNullOrEmpty(secret)) return text;
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { secret };
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = secret };
            foreach (string key in builder.Keys)
            {
                var value = Convert.ToString(builder[key], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(value)) tokens.Add(value);
            }
        }
        catch (ArgumentException) { /* Synthetic values need not be connection strings. */ }
        foreach (var token in tokens.OrderByDescending(value => value.Length))
        {
            foreach (var form in new[] { token, JsonSerializer.Serialize(token)[1..^1],
                token.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) })
                text = text.Replace(form, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    internal static async Task WithCleanupAsync(string ownedDirectory, string databaseOwnership, Func<Task> body, Func<Task> databaseCleanup,
        Action fileCleanup, Action<string>? report = null)
    {
        var errors = new List<Exception>();
        var databaseCompleted = false;
        var filesDeleted = false;
        try { await body(); }
        catch (Exception error) { errors.Add(new InvalidOperationException("CLI body failed (" + error.GetType().Name + "). Origin: " + error.StackTrace)); }
        finally
        {
            try { await databaseCleanup(); databaseCompleted = true; }
            catch (Exception error) { errors.Add(new InvalidOperationException("CLI database cleanup failed (" + error.GetType().Name + "); owned objects: " + databaseOwnership + ".")); }
            finally
            {
                try { fileCleanup(); filesDeleted = true; }
                catch (Exception error) { errors.Add(new InvalidOperationException("CLI private directory cleanup failed (" + error.GetType().Name + "); owned directory: " + ownedDirectory + ".")); }
            }
        }
        report?.Invoke("CLI cleanup: owned directory=" + ownedDirectory + "; owned objects=" + databaseOwnership + "; database="
            + (databaseCompleted ? "completed" : "failed") + "; files=" + (filesDeleted ? "deleted" : "failed") + ".");
        if (errors.Count > 0) throw new AggregateException("CLI fixture failed; cleanup results are included.", errors);
    }

    internal static string HostSource(bool configuration = true) => configuration ? """
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Configuration;
        partial class Program
        {
            public static IHostBuilder CreateHostBuilder(string[] args)
                => new HostBuilder().ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(
                    new Dictionary<string, string?> {
                        ["ConnectionStrings:DamengCliFixture"] = Environment.GetEnvironmentVariable("ConnectionStrings__DamengCliFixture")
                    }));
        }
        """ : "";

    internal static string ContextConfigurationSource(string providerMethod) => $$"""
        using Microsoft.EntityFrameworkCore;
        namespace CliRoundtrip.Scaffolded;
        public partial class ScaffoldedCliContext
        {
            public ScaffoldedCliContext() : base(new DbContextOptions<ScaffoldedCliContext>()) { }
            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.{{providerMethod}}(Environment.GetEnvironmentVariable("ConnectionStrings__DamengCliFixture")
                    ?? throw new InvalidOperationException("CLI connection configuration missing."));
        }
        """;
}
