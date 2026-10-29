using System.Data.Common;
using System.Text.Json;
using System.Xml.Linq;
using W.EntityFrameworkCore.Dameng.FunctionalTests;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengCliFixtureTests
{
    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    [Fact]
    public void ToolVersionComparisonUsesPrivateRawOutputWhenShortUserCollides()
    {
        const string secret = "Server=fixture.invalid;User ID=10;Password=synthetic-version-only";
        var result = new DamengCliTestHarness.ProcessResult(0, "EF tools\n10.0.12\n" + secret, secret);
        Assert.True(result.HasExpectedToolVersion("10.0.12"), "Locked tool version was changed by diagnostic redaction.");
        Assert.False(result.HasExpectedToolVersion("10.0.11"));
        Assert.True(!result.SafeOutput.Contains("10.0.12", StringComparison.Ordinal), "Synthetic version collision was not exercised.");
        AssertSafe(secret, result.SafeOutput + result);
    }

    [Theory]
    [InlineData("port")]
    [InlineData("option")]
    [InlineData("short-user")]
    public void SemanticIdentityChecksSurviveRedactionCollisionsWithoutExposingRawOutput(string collision)
    {
        const string mvid = "1756fef6-b801-4182-91ab-ed3ef6945264";
        var sha256 = "fe4182" + new string('a', 58);
        var password = "synthetic\\quote\";秘密-" + Guid.NewGuid().ToString("N");
        var builder = new DbConnectionStringBuilder
        {
            ["Server"] = "fixture.invalid",
            ["User ID"] = collision == "short-user" ? "fe" : "synthetic-user",
            ["Password"] = password
        };
        var token = collision == "short-user" ? "fe" : "4182";
        if (collision == "port") builder["Port"] = token;
        if (collision == "option") builder["Max Pool Size"] = token;
        var secret = builder.ConnectionString;
        var csharp = secret.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var raw = "W.DamengProvider MVID=" + mvid + "\nSHA256=" + sha256 + "\n" + secret
            + "\n" + JsonSerializer.Serialize(secret) + "\n" + csharp + "\n" + password;
        var result = new DamengCliTestHarness.ProcessResult(0, raw, secret);

        // These checks deliberately inspect only the internal raw buffer and throw a fixed message.
        result.RequireExpectedOutput("W.DamengProvider MVID=" + mvid);
        result.RequireExpectedOutput("SHA256=" + sha256);
        Assert.True(!result.SafeOutput.Contains(mvid, StringComparison.Ordinal)
            && !result.SafeOutput.Contains(sha256, StringComparison.Ordinal), "Synthetic collision was not exercised.");
        Assert.True(!result.SafeOutput.Contains(token, StringComparison.OrdinalIgnoreCase), "A synthetic connection field reached diagnostics.");
        AssertSafe(secret, result.SafeOutput + result);
        AssertSafe(password, result.SafeOutput + result);
        var error = Assert.Throws<InvalidOperationException>(() => result.RequireExpectedOutput("missing identity"));
        Assert.Equal("CLI output did not contain the expected assembly identity.", error.Message);
        AssertSafe(secret, error.ToString());
        AssertSafe(password, error.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingWrongToolVersionIsUpdatedAndRecheckedOrRejected(bool updateWorks)
    {
        var directory = DamengCliTestHarness.CreatePrivateDirectory();
        try
        {
            var toolDirectory = Path.Combine(directory, "artifacts", "dotnet-ef-tool");
            Directory.CreateDirectory(toolDirectory);
            var tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "dotnet-ef.exe" : "dotnet-ef");
            File.WriteAllText(tool, "synthetic tool");
            var queries = 0;
            var updates = 0;
            Task<DamengCliTestHarness.ProcessResult> Execute(System.Diagnostics.ProcessStartInfo request)
            {
                Assert.False(request.Environment.ContainsKey("DAMENG_TEST_CONNECTION_STRING"));
                Assert.False(request.Environment.ContainsKey(DamengCliTestHarness.ConnectionVariable));
                if (request.ArgumentList.Count == 1 && request.ArgumentList[0] == "--version")
                {
                    queries++;
                    return Task.FromResult(new DamengCliTestHarness.ProcessResult(0, queries > 1 && updateWorks ? "10.0.12\n" : "10.0.11\n"));
                }
                Assert.Equal(new[] { "tool", "update", "dotnet-ef", "--version", "10.0.12", "--tool-path", toolDirectory }, request.ArgumentList);
                updates++;
                return Task.FromResult(new DamengCliTestHarness.ProcessResult(0, "synthetic update"));
            }
            if (updateWorks)
                Assert.Equal(tool, await DamengCliTestHarness.EnsureToolAsync("synthetic-dotnet", directory, "10.0.12", Execute));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => DamengCliTestHarness.EnsureToolAsync("synthetic-dotnet", directory, "10.0.12", Execute));
            Assert.Equal(2, queries);
            Assert.Equal(1, updates);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MissingParentCliHomeUsesAndCleansPrivateChildHome()
    {
        var request = DamengCliTestHarness.CreateProcess(Dotnet, Path.GetTempPath(), ["--version"], null);
        request.Environment.Remove("DOTNET_CLI_HOME");
        var result = await DamengCliTestHarness.RunAsync(request);
        Assert.Equal(0, result.ExitCode);
        Assert.True(request.Environment.TryGetValue("DOTNET_CLI_HOME", out var ownedHome) && !string.IsNullOrWhiteSpace(ownedHome));
        Assert.False(Directory.Exists(ownedHome));
    }

    [Fact]
    public void LoadedAssemblyIdentityWinsOverStaleDebugCandidate()
    {
        var assembly = typeof(W.EntityFrameworkCore.Dameng.Infrastructure.DamengDriver).Assembly;
        var identity = DamengCliTestHarness.CaptureAssembly(assembly, "W.EntityFrameworkCore.Dameng");
        var directory = DamengCliTestHarness.CreatePrivateDirectory();
        try
        {
            var stale = Path.Combine(directory, "src", "provider", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stale, identity.Name + ".dll"), "stale candidate");
            Assert.Equal(assembly.Location, identity.Location);
            Assert.NotEqual(Path.Combine(stale, identity.Name + ".dll"), identity.Location);
            Assert.Throws<InvalidOperationException>(() => DamengCliTestHarness.CaptureFile("", identity.Name));
            Assert.Throws<InvalidOperationException>(() => DamengCliTestHarness.CaptureAssembly(assembly, "Wrong.Provider"));
            Assert.Throws<InvalidOperationException>(() => DamengCliTestHarness.VerifyIdentity(identity, identity with { Mvid = Guid.NewGuid() }));
            Assert.Throws<InvalidOperationException>(() => DamengCliTestHarness.VerifyIdentity(identity, identity with { Sha256 = new string('0', 64) }));
            File.Copy(identity.Location, Path.Combine(directory, identity.Name + ".dll"));
            DamengCliTestHarness.VerifyNestedCopies([identity], directory);
            var copied = Path.Combine(directory, identity.Name + ".dll");
            using (var file = new FileStream(copied, FileMode.Append, FileAccess.Write)) file.WriteByte(0);
            Assert.Equal(assembly.GetName().Version, System.Reflection.AssemblyName.GetAssemblyName(copied).Version);
            var changed = DamengCliTestHarness.CaptureFile(copied, identity.Name);
            Assert.Equal(identity.Mvid, changed.Mvid);
            Assert.NotEqual(identity.Sha256, changed.Sha256);
            Assert.Throws<InvalidOperationException>(() => DamengCliTestHarness.VerifyNestedCopies([identity], directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ProviderCodeGenerationUsesPublicMethodAndSafeAlias()
    {
#pragma warning disable EF1001 // Exercise the provider code-generation contract with a safe alias.
        var generator =
            new W.EntityFrameworkCore.Dameng.Scaffolding.Internal.DamengCodeGenerator(
                new Microsoft.EntityFrameworkCore.Scaffolding.ProviderCodeGeneratorDependencies([]),
                W.EntityFrameworkCore.Dameng.Infrastructure.Internal.DamengOfficialDriver.Instance);
        var call = generator.GenerateUseProvider(DamengCliTestHarness.ConnectionAlias, null);
        Assert.Equal("UseDameng", call.Method);
        Assert.Equal("DamengDbContextOptionsBuilderExtensions", call.DeclaringType);
        Assert.Equal(DamengCliTestHarness.ConnectionAlias, Assert.Single(call.Arguments));
#pragma warning restore EF1001
    }

    [Fact]
    public void ProcessBoundaryAndGeneratedSourcesNeverContainSyntheticSecret()
    {
        var secret = "Server=unit.invalid;User ID=合成;Password=\"a\\b\"\"c;秘密\"";
        var request = DamengCliTestHarness.CreateProcess(Dotnet, Path.GetTempPath(),
            ["dbcontext", "scaffold", DamengCliTestHarness.ConnectionAlias, "provider", "--no-onconfiguring"], secret);
        var sources = DamengCliTestHarness.HostSource() + DamengCliTestHarness.ContextConfigurationSource("UseDameng");
        Assert.True(request.Environment[DamengCliTestHarness.ConnectionVariable] == secret, "Required environment value missing.");
        Assert.False(request.Environment.ContainsKey("DAMENG_TEST_CONNECTION_STRING"));
        AssertSafe(secret, string.Join(' ', request.ArgumentList) + sources);
        var forms = secret + JsonSerializer.Serialize(secret) + secret.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        AssertSafe(secret, DamengCliTestHarness.Redact(secret, forms));
        foreach (var verb in new[] { "restore", "build", "tool", "--version" })
        {
            var isolated = DamengCliTestHarness.CreateProcess(Dotnet, Path.GetTempPath(), [verb], null);
            Assert.False(isolated.Environment.ContainsKey(DamengCliTestHarness.ConnectionVariable));
            Assert.False(isolated.Environment.ContainsKey("DAMENG_TEST_CONNECTION_STRING"));
        }
    }

    [Theory]
    [InlineData("body")]
    [InlineData("database")]
    [InlineData("files")]
    [InlineData("both")]
    public async Task CleanupFailuresAreIndependentAndFailClosed(string failure)
    {
        var directory = DamengCliTestHarness.CreatePrivateDirectory();
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        var secret = "synthetic-private-failure-" + Guid.NewGuid().ToString("N");
        var ownership = "synthetic-owned-objects-" + Guid.NewGuid().ToString("N");
        var reports = new List<string>();
        var databaseAttempted = false;
        var fileAttempted = false;
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => DamengCliTestHarness.WithCleanupAsync(directory, ownership,
                () => failure is "body" or "both" ? Task.FromException(new InvalidOperationException(secret)) : Task.CompletedTask,
                () => { databaseAttempted = true; return failure is "database" or "both" ? Task.FromException(new IOException(secret)) : Task.CompletedTask; },
                () => { fileAttempted = true; if (failure == "files") throw new IOException(secret); Directory.Delete(directory, true); }, reports.Add));
            Assert.Equal(failure == "both" ? 2 : 1, error.InnerExceptions.Count);
            Assert.True(databaseAttempted && fileAttempted);
            var summary = Assert.Single(reports);
            Assert.Contains(directory, summary, StringComparison.Ordinal);
            Assert.Contains("; database=" + (failure is "database" or "both" ? "failed" : "completed"), summary, StringComparison.Ordinal);
            Assert.Contains("; files=" + (failure == "files" ? "failed" : "deleted"), summary, StringComparison.Ordinal);
            if (failure == "files") Assert.Contains(directory, error.Message, StringComparison.Ordinal);
            if (failure is "database" or "both") Assert.Contains(ownership, error.Message, StringComparison.Ordinal);
            AssertSafe(secret, error.ToString() + string.Join("\n", reports));
            Assert.Equal(failure == "files", Directory.Exists(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ActualEfDesignResolvesOnlyNamedEnvironmentConfiguration()
    {
        var root = FindRoot();
        var packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        string Version(string name) => packages.Descendants("PackageVersion").Single(e => (string?)e.Attribute("Include") == name)
            .Attribute("Version")!.Value.TrimStart('[').Split(',')[0].TrimEnd(']');
        var efVersion = Version("Microsoft.EntityFrameworkCore.Relational");
        var dmVersion = Version("DM.DmProvider");
        var tool = await DamengCliTestHarness.EnsureToolAsync(Dotnet, root, efVersion);
        var providerIdentity = DamengCliTestHarness.CaptureAssembly(typeof(W.EntityFrameworkCore.Dameng.Infrastructure.DamengDriver).Assembly, "W.EntityFrameworkCore.Dameng");
        var provider = providerIdentity.Location;
        var directory = DamengCliTestHarness.CreatePrivateDirectory();
        var secret = "synth\\quote\";非ASCII-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(Path.Combine(directory, "Synthetic.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <Reference Include="W.EntityFrameworkCore.Dameng"><HintPath>{{System.Security.SecurityElement.Escape(provider)}}</HintPath></Reference>
                    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="{{efVersion}}" />
                    <PackageReference Include="DM.DmProvider" Version="{{dmVersion}}" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(directory, "Program.cs"), DamengCliTestHarness.IdentityVerificationSource([providerIdentity]));
            File.WriteAllText(Path.Combine(directory, "FixtureServices.cs"), """
                using System.Data.Common;
                using Microsoft.EntityFrameworkCore.Design;
                using Microsoft.EntityFrameworkCore.Scaffolding;
                using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
                using Microsoft.Extensions.DependencyInjection;
                public sealed class FixtureServices : IDesignTimeServices
                {
                    public void ConfigureDesignTimeServices(IServiceCollection services)
                        => services.AddSingleton<IDatabaseModelFactory, RecordingFactory>();
                }
                public sealed class RecordingFactory : IDatabaseModelFactory
                {
                    public DatabaseModel Create(string connection, DatabaseModelFactoryOptions options)
                    {
                        var expected = Environment.GetEnvironmentVariable("ConnectionStrings__DamengCliFixture");
                        if (expected is null || connection != expected || connection.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Named configuration was not resolved.");
                        File.WriteAllText("factory-called.txt", "resolved without network");
                        var model = new DatabaseModel { DatabaseName = "Synthetic" };
                        var table = new DatabaseTable { Database = model, Name = "Fixture" };
                        var column = new DatabaseColumn { Table = table, Name = "Id", StoreType = "INT", IsNullable = false };
                        table.Columns.Add(column);
                        table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = "PK_Fixture" };
                        table.PrimaryKey.Columns.Add(column);
                        model.Tables.Add(table);
                        return model;
                    }
                    public DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
                        => throw new InvalidOperationException("Network path forbidden in synthetic fixture.");
                }
                """);
            await RequireSuccess(Dotnet, directory, ["restore", "--disable-parallel", "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false"]);
            foreach (var scenario in new[] { "valid", "missing", "alias", "host" })
            {
                File.Delete(Path.Combine(directory, "factory-called.txt"));
                File.WriteAllText(Path.Combine(directory, "Host.cs"), DamengCliTestHarness.HostSource(scenario != "host"));
                await RequireSuccess(Dotnet, directory, ["build", "--no-restore", "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false", "--disable-build-servers"]);
                DamengCliTestHarness.VerifyNestedCopies([providerIdentity], Path.Combine(directory, "bin", "Debug", "net10.0"));
                await RequireSuccess(Dotnet, directory, ["run", "--no-build"]);
                var alias = scenario == "alias" ? "Name=ConnectionStrings:UnknownFixture" : DamengCliTestHarness.ConnectionAlias;
                var result = await DamengCliTestHarness.RunAsync(DamengCliTestHarness.CreateProcess(tool, directory,
                    ["dbcontext", "scaffold", alias, "W.EntityFrameworkCore.Dameng", "--no-build", "--no-onconfiguring", "--force", "--output-dir", "Generated"],
                    scenario == "missing" ? null : secret), secret);
                Assert.True((result.ExitCode == 0) == (scenario == "valid"), "Actual EF Design scenario failed: " + scenario + "; " + result.SafeOutput);
                Assert.Equal(scenario == "valid", File.Exists(Path.Combine(directory, "factory-called.txt")));
                AssertSafe(secret, result.SafeOutput);
                foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)) AssertSafe(secret, File.ReadAllText(file));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void AssertSafe(string secret, string text)
    {
        Assert.True(!text.Contains(secret, StringComparison.Ordinal)
            && !text.Contains(JsonSerializer.Serialize(secret)[1..^1], StringComparison.Ordinal)
            && !text.Contains(secret.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal), StringComparison.Ordinal),
            "Synthetic value crossed a forbidden boundary.");
    }

    private static async Task RequireSuccess(string executable, string directory, string[] args)
    {
        var result = await DamengCliTestHarness.RunAsync(DamengCliTestHarness.CreateProcess(executable, directory, args, null));
        Assert.True(result.ExitCode == 0, "Synthetic fixture tool failed: " + result.SafeOutput);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "W.EntityFrameworkCore.Dameng.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Fixture repository root missing.");
    }
}
