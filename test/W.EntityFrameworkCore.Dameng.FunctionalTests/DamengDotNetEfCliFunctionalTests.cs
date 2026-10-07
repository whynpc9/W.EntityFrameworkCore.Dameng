using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dm;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// End-to-end coverage for the dotnet ef command line: migrations add/script/database update
/// against the real server, then dbcontext scaffold back from it.
/// </summary>
public sealed class DamengDotNetEfCliFunctionalTests(ITestOutputHelper output)
{
    private const int CommandTimeoutSeconds = 300;

    private static readonly string[] DebugEnvironmentVariableNames =
    [
        "DOTNET_ROOT",
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "DOTNET_CLI_HOME",
        "NUGET_PACKAGES",
        "DOTNET_MULTILEVEL_LOOKUP"
    ];

    [DamengFact]
    public async Task MigrationsAndScaffoldRunThroughDotNetEfCli()
    {
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_CLI_{suffix}";
        var sequenceName = $"EF10_CLISQ_{suffix}";
        var standaloneSequenceName = $"EF10_CLISO_{suffix}";
        var historyTableName = $"EF10_CLIH_{suffix}";
        var principalTableName = $"EF10_CLIP_{suffix}";
        var dependentTableName = $"EF10_CLIC_{suffix}";
        var viewName = $"EF10_CLIV_{suffix}";

        var repoRoot = FindRepositoryRoot();
        var (efCoreVersion, dmProviderVersion) = ResolveLockedPackageVersions(repoRoot);
        var providerAssembly = Path.Combine(
            repoRoot,
            "src",
            "W.EntityFrameworkCore.Dameng",
            "bin",
            "Debug",
            "net10.0",
            "W.EntityFrameworkCore.Dameng.dll");
        Assert.True(
            File.Exists(providerAssembly),
            "Build the provider before running the CLI test: " + providerAssembly);
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dameng-ef-cli-{suffix.ToLowerInvariant()}");

        var dotnetEf = await EnsureDotNetEfToolAsync(repoRoot, efCoreVersion);

        Directory.CreateDirectory(projectDirectory);
        try
        {
            WriteCliProject(
                projectDirectory,
                providerAssembly,
                efCoreVersion,
                dmProviderVersion,
                tableName,
                sequenceName,
                suffix);

            var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(dotnetHost))
            {
                dotnetHost = "dotnet";
            }

            await RunDotNetAsync(
                dotnetHost,
                projectDirectory,
                connectionString,
                ["restore", "--disable-parallel"]);
            // dotnet-ef evaluates and builds the project in its own process; that path is
            // unreliable when hosted under dotnet test (ResolvePackageAssets fails with a
            // NuGet LockFile NRE), so the build is done explicitly and ef runs --no-build.
            await RunDotNetAsync(
                dotnetHost,
                projectDirectory,
                connectionString,
                ["build", "--no-restore"]);

            await RunDotNetEfAsync(
                dotnetEf,
                projectDirectory,
                connectionString,
                ["migrations", "add", "InitialCreate", "--no-build", "--output-dir", "Migrations"]);
            var migrationFile = Assert.Single(
                Directory.GetFiles(Path.Combine(projectDirectory, "Migrations"), "*_InitialCreate.cs"));

            // With --no-build the new migration must be compiled before scripting or applying.
            await RunDotNetAsync(
                dotnetHost,
                projectDirectory,
                connectionString,
                ["build", "--no-restore"]);

            await RunDotNetEfAsync(
                dotnetEf,
                projectDirectory,
                connectionString,
                ["migrations", "script", "--idempotent", "--no-build", "--output", "migrate.sql"]);
            var script = File.ReadAllText(Path.Combine(projectDirectory, "migrate.sql"));
            Assert.True(
                script.Contains($"COMMENT ON TABLE \"{tableName}\"", StringComparison.Ordinal),
                "Idempotent script must contain the table comment statement.");
            Assert.True(
                script.Contains("IDENTITY(5,2)", StringComparison.Ordinal),
                "Idempotent script must contain the identity facet.");
            Assert.True(
                script.Contains($"{sequenceName}\".NEXTVAL", StringComparison.Ordinal),
                "Idempotent script must contain the sequence default.");

            await RunDotNetEfAsync(
                dotnetEf,
                projectDirectory,
                connectionString,
                ["database", "update", "--no-build"]);

            await using (var connection = new DmConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var createStandalone = connection.CreateCommand();
                createStandalone.CommandText = $"CREATE SEQUENCE \"{standaloneSequenceName}\" START WITH 73 INCREMENT BY 5 MINVALUE 1 MAXVALUE 1000 NOCACHE NOORDER";
                await createStandalone.ExecuteNonQueryAsync();
                createStandalone.CommandText = $"CREATE TABLE \"{principalTableName}\" (ID INT NOT NULL, TENANT INT NOT NULL, CODE INT NOT NULL, NAME NVARCHAR2(20), CONSTRAINT \"PK_CLIP_{suffix}\" NOT CLUSTER PRIMARY KEY(ID), CONSTRAINT \"UQ_CLIP_{suffix}\" UNIQUE(TENANT,CODE)) STORAGE(CLUSTERBTR, FILLFACTOR 85)";
                await createStandalone.ExecuteNonQueryAsync();
                createStandalone.CommandText = $"CREATE TABLE \"{dependentTableName}\" (ID INT NOT NULL, TENANT INT, CODE INT, PARENT_ID INT, CONSTRAINT \"PK_CLIC_{suffix}\" NOT CLUSTER PRIMARY KEY(ID), CONSTRAINT \"FK_CLIC_{suffix}\" FOREIGN KEY(TENANT,CODE) REFERENCES \"{principalTableName}\"(TENANT,CODE) ON DELETE CASCADE, CONSTRAINT \"FK_SELF_{suffix}\" FOREIGN KEY(PARENT_ID) REFERENCES \"{dependentTableName}\"(ID)) STORAGE(CLUSTERBTR)";
                await createStandalone.ExecuteNonQueryAsync();
                createStandalone.CommandText = $"CREATE VIEW \"{viewName}\" AS SELECT ID,CODE FROM \"{principalTableName}\"";
                await createStandalone.ExecuteNonQueryAsync();
                createStandalone.CommandText = $"ALTER TABLE \"{tableName}\" ADD COMPUTED_CODE AS (UPPER(CODE))";
                await createStandalone.ExecuteNonQueryAsync();
                createStandalone.CommandText = $"ALTER TABLE \"{tableName}\" ADD EXPLICIT_WIDE CHAR(476 CHAR)";
                await createStandalone.ExecuteNonQueryAsync();
                Assert.Equal(
                    "命令行订单表",
                    await ScalarStringAsync(
                        connection,
                        "SELECT COMMENTS FROM USER_TAB_COMMENTS WHERE TABLE_NAME = :name",
                        tableName));
                Assert.Equal(
                    "编码",
                    await ScalarStringAsync(
                        connection,
                        "SELECT COMMENTS FROM USER_COL_COMMENTS WHERE TABLE_NAME = :name AND COLUMN_NAME = 'CODE'",
                        tableName));
                Assert.Equal(
                    "5",
                    await ScalarStringAsync(connection, $"SELECT IDENT_SEED('{tableName}') FROM dual", null));
                Assert.Equal(
                    "1",
                    await ScalarStringAsync(
                        connection,
                        $"SELECT COUNT(*) FROM \"{historyTableName}\"",
                        null));
            }

            Assert.Contains("CreateTable", File.ReadAllText(migrationFile), StringComparison.Ordinal);

            await RunDotNetEfAsync(
                dotnetEf,
                projectDirectory,
                connectionString,
                [
                    "dbcontext", "scaffold", connectionString,
                    "W.EntityFrameworkCore.Dameng",
                    "--no-build",
                    "--table", tableName,
                    "--table", principalTableName,
                    "--table", dependentTableName,
                    "--table", viewName,
                    "--context", "ScaffoldedCliContext",
                    "--output-dir", "Scaffolded",
                    "--force"
                ]);

            var scaffoldedContext = File.ReadAllText(
                Path.Combine(projectDirectory, "Scaffolded", "ScaffoldedCliContext.cs"));
            output.WriteLine(Redact(connectionString, scaffoldedContext));
            Assert.Contains("UseDameng(", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains(
                $".ToTable(\"{tableName}\"",
                scaffoldedContext,
                StringComparison.Ordinal);
            Assert.True(
                scaffoldedContext.Contains("HasComment(\"命令行订单表\")", StringComparison.Ordinal),
                "Scaffolded context must carry the table comment.");
            Assert.True(
                scaffoldedContext.Contains("HasComment(\"编码\")", StringComparison.Ordinal),
                "Scaffolded context must carry the column comment.");
            Assert.True(
                scaffoldedContext.Contains("UseDamengIdentityColumn(5L, 2)", StringComparison.Ordinal),
                "Scaffolded context must use the public identity API with catalog facets.");
            Assert.True(
                scaffoldedContext.Contains(
                    $"UseDamengSequence(\"{sequenceName}\",",
                    StringComparison.Ordinal),
                "Scaffolded context must use the public sequence API.");
            Assert.True(
                scaffoldedContext.Contains("HasSequence", StringComparison.Ordinal)
                && scaffoldedContext.Contains(sequenceName, StringComparison.Ordinal)
                && scaffoldedContext.Contains("StartsAt(41", StringComparison.Ordinal)
                && scaffoldedContext.Contains("IncrementsBy(3", StringComparison.Ordinal),
                "Scaffolded context must carry the sequence catalog facets, not invented defaults.");
            Assert.True(
                scaffoldedContext.Contains("IsDescending()", StringComparison.Ordinal),
                "Scaffolded context must keep the descending index.");

            Assert.Contains(standaloneSequenceName, scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("StartsAt(73", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("IncrementsBy(5", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("HasAnnotation(\"Dameng:IsClustered\", false)", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("HasAnnotation(\"Dameng:IsClusterBtree\", true)", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("HasAnnotation(\"Dameng:TableFillFactor\", 85)", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("HasAnnotation(\"Dameng:IndexFillFactor\", 70)", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("HasColumnType(\"VARCHAR2(40 BYTE)\")", scaffoldedContext, StringComparison.Ordinal);
            Assert.Contains("HasColumnType(\"CHAR(476 CHAR)\")", scaffoldedContext, StringComparison.Ordinal);
            WriteScaffoldModelAssertions(projectDirectory, tableName, principalTableName, dependentTableName, viewName);
            await RunDotNetAsync(dotnetHost, projectDirectory, connectionString,
                ["build", "--no-restore", "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false", "--disable-build-servers"]);
            await RunDotNetAsync(dotnetHost, projectDirectory, connectionString, ["run", "--no-build"]);
            await RunDotNetEfAsync(dotnetEf, projectDirectory, connectionString,
                ["dbcontext", "script", "--context", "ScaffoldedCliContext", "--no-build", "--output", "scaffolded-create.sql"]);
            Assert.Contains("NOT CLUSTER PRIMARY KEY", File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql")), StringComparison.Ordinal);
            Assert.Contains("STORAGE(CLUSTERBTR, FILLFACTOR 85)", File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql")), StringComparison.Ordinal);
            Assert.Contains("STORAGE(FILLFACTOR 70)", File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql")), StringComparison.Ordinal);
            Assert.Contains("VARCHAR2(40 BYTE)", File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql")), StringComparison.Ordinal);
            Assert.Contains("CHAR(476 CHAR)", File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql")), StringComparison.Ordinal);
            Assert.Contains("SF_GET_LENGTH_IN_CHAR()", File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql")), StringComparison.Ordinal);
            var scaffoldedSql = File.ReadAllText(Path.Combine(projectDirectory, "scaffolded-create.sql"));
            Assert.Contains(standaloneSequenceName, scaffoldedSql, StringComparison.Ordinal);
            Assert.Contains("START WITH 73 INCREMENT BY 5", scaffoldedSql, StringComparison.Ordinal);

            var scaffoldedEntity = File.ReadAllText(
                Assert.Single(
                    Directory.GetFiles(Path.Combine(projectDirectory, "Scaffolded"), "*.cs"),
                    path => !path.EndsWith("ScaffoldedCliContext.cs", StringComparison.Ordinal)
                        && File.ReadAllText(path).Contains("public long Id", StringComparison.Ordinal)));
            Assert.Contains("public long Id", scaffoldedEntity, StringComparison.Ordinal);
            Assert.Contains("public string Code", scaffoldedEntity, StringComparison.Ordinal);
            Assert.Contains("public decimal Amount", scaffoldedEntity, StringComparison.Ordinal);
            Assert.Contains("HasDefaultValueSql(", scaffoldedContext, StringComparison.Ordinal);
        }
        finally
        {
            await using (var connection = new DmConnection(connectionString))
            {
                await connection.OpenAsync();
                await DropIfExistsAsync(connection, "USER_VIEWS", "VIEW_NAME", viewName);
                await DropIfExistsAsync(connection, "USER_TABLES", "TABLE_NAME", dependentTableName);
                await DropIfExistsAsync(connection, "USER_TABLES", "TABLE_NAME", principalTableName);
                await DropIfExistsAsync(connection, "USER_TABLES", "TABLE_NAME", tableName);
                await DropIfExistsAsync(connection, "USER_TABLES", "TABLE_NAME", historyTableName);
                await DropIfExistsAsync(connection, "USER_SEQUENCES", "SEQUENCE_NAME", sequenceName);
                await DropIfExistsAsync(connection, "USER_SEQUENCES", "SEQUENCE_NAME", standaloneSequenceName);
            }

            TryDeleteDirectory(projectDirectory);
        }
    }

    private static void WriteScaffoldModelAssertions(string directory, string table, string principal, string dependent, string view)
        => File.WriteAllText(Path.Combine(directory, "Program.cs"), $$"""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Metadata;
            using CliRoundtrip.Scaffolded;

            using var context = new ScaffoldedCliContext();
            var model = context.GetService<IDesignTimeModel>().Model;
            var source = model.GetEntityTypes().Single(entity => entity.GetTableName() == "{{table}}");
            var parent = model.GetEntityTypes().Single(entity => entity.GetTableName() == "{{principal}}");
            var child = model.GetEntityTypes().Single(entity => entity.GetTableName() == "{{dependent}}");
            var projection = model.GetEntityTypes().Single(entity => entity.GetViewName() == "{{view}}");
            void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }
            Require(!parent.FindProperty("Code")!.IsNullable, "Principal nullability changed.");
            Require(parent.FindProperty("Name")!.IsNullable, "Nullable text changed.");
            Require(child.FindProperty("Code")!.IsNullable, "Dependent nullability changed.");
            var composite = child.GetForeignKeys().Single(key => key.PrincipalEntityType == parent);
            Require(composite.Properties.Select(property => property.Name).SequenceEqual(new[] { "Tenant", "Code" }), "Dependent column order changed.");
            Require(composite.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(new[] { "Tenant", "Code" }), "Principal column order changed.");
            Require(composite.DeleteBehavior == DeleteBehavior.Cascade, "Delete action changed.");
            Require(child.GetForeignKeys().Count(key => key.PrincipalEntityType == child) == 1, "Self reference lost.");
            Require(projection.FindPrimaryKey() is null, "View must remain keyless.");
            Require((bool?)source.FindPrimaryKey()!["Dameng:IsClustered"] == false, "Clustering annotation lost.");
            Require((bool?)source["Dameng:IsClusterBtree"] == true, "Storage annotation lost.");
            Require((int?)source["Dameng:TableFillFactor"] == 85, "Table fill factor lost.");
            Require(source.GetIndexes().Any(index => (int?)index["Dameng:IndexFillFactor"] == 70), "Index fill factor lost.");
            Require((long?)source.FindProperty("Id")!["Dameng:IdentitySeed"] == 5L, "Identity seed lost.");
            Require((int?)source.FindProperty("Id")!["Dameng:IdentityIncrement"] == 2, "Identity increment lost.");
            var computed = source.GetProperties().Single(property => property.GetColumnName() == "COMPUTED_CODE");
            Require(computed.GetComputedColumnSql()!.Contains("UPPER", StringComparison.Ordinal), "Computed expression lost.");
            Require(computed.GetDefaultValueSql() is null, "Computed expression became an insert default.");
            Require(computed.GetIsStored() == false, "Virtual computed column became stored.");
            Require(computed.ValueGenerated == ValueGenerated.OnAddOrUpdate, "Computed generation behavior changed.");
            Require(source.FindProperty("Note")!.GetColumnType() == "VARCHAR2(40 BYTE)", "Byte length semantics changed.");
            Console.WriteLine("Scaffolded model assertions passed.");
            """);

    // A project reference would drag the repository's lock-file props into the temp project
    // graph and break NuGet asset resolution; reference the built provider assembly instead.
    private static void WriteCliProject(
        string projectDirectory,
        string providerAssembly,
        string efCoreVersion,
        string dmProviderVersion,
        string tableName,
        string sequenceName,
        string suffix)
    {
        File.WriteAllText(
            Path.Combine(projectDirectory, "CliRoundtrip.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="W.EntityFrameworkCore.Dameng">
                  <HintPath>{providerAssembly}</HintPath>
                </Reference>
                <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="{efCoreVersion}" />
                <PackageReference Include="DM.DmProvider" Version="{dmProviderVersion}" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(projectDirectory, "Program.cs"),
            """Console.WriteLine("cli roundtrip");""");

        File.WriteAllText(
            Path.Combine(projectDirectory, "CliDbContext.cs"),
            $$"""
            using Microsoft.EntityFrameworkCore;

            public sealed class CliOrder
            {
                public long Id { get; set; }

                public string Code { get; set; } = "";

                public long Number { get; set; }

                public decimal Amount { get; set; }

                public string? Note { get; set; }
            }

            public sealed class CliDbContext : DbContext
            {
                public DbSet<CliOrder> Orders => Set<CliOrder>();

                protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                    => optionsBuilder.UseDameng(
                        Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")!,
                        dameng => dameng.MigrationsHistoryTable("EF10_CLIH_{{suffix}}"));

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.HasSequence<long>("{{sequenceName}}").StartsAt(41).IncrementsBy(3);

                    modelBuilder.Entity<CliOrder>(entity =>
                    {
                        entity.ToTable("{{tableName}}", table => table.HasComment("命令行订单表"));
                        entity.HasAnnotation("Dameng:TableFillFactor", 85);
                        entity.HasKey(item => item.Id).HasAnnotation("Dameng:IsClustered", false);
                        entity.Property(item => item.Id).UseDamengIdentityColumn(5, 2);
                        entity.Property(item => item.Code)
                            .HasColumnName("CODE")
                            .HasMaxLength(30)
                            .HasComment("编码");
                        entity.Property(item => item.Number)
                            .HasColumnName("NUMBER")
                            .UseDamengSequence("{{sequenceName}}");
                        entity.Property(item => item.Amount)
                            .HasColumnType("DECIMAL(18,2)")
                            .HasDefaultValueSql("\"{{sequenceName}}\".NEXTVAL");
                        entity.Property(item => item.Note)
                            .HasColumnName("NOTE")
                            .HasColumnType("VARCHAR2(40 BYTE)");
                        entity.HasIndex(item => item.Code)
                            .HasDatabaseName("IDX_CLI_{{suffix}}")
                            .HasAnnotation("Dameng:IndexFillFactor", 70)
                            .IsDescending();
                    });
                }
            }
            """);

    }

    private async Task RunDotNetEfAsync(
        string dotnetEf,
        string workingDirectory,
        string connectionString,
        string[] arguments)
    {
        var efArguments = new List<string>();
        efArguments.AddRange(arguments);
        await RunDotNetAsync(dotnetEf, workingDirectory, connectionString, efArguments);
    }

    private async Task RunDotNetAsync(
        string dotnet,
        string workingDirectory,
        string connectionString,
        List<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (arguments[0] is "build" or "restore")
        {
            foreach (var flag in new[] { "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false" })
            {
                if (!startInfo.ArgumentList.Contains(flag))
                {
                    startInfo.ArgumentList.Add(flag);
                }
            }

            if (arguments[0] == "build" && !startInfo.ArgumentList.Contains("--disable-build-servers"))
            {
                startInfo.ArgumentList.Add("--disable-build-servers");
            }
        }

        startInfo.Environment["DAMENG_TEST_CONNECTION_STRING"] = connectionString;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet ef.");

        // Drain both pipes concurrently and bound the whole wait; a child that fills one
        // pipe must not block the other reader or outrun the timeout.
        var readOutput = process.StandardOutput.ReadToEndAsync();
        var readError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(CommandTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                Redact(connectionString, $"dotnet {string.Join(' ', arguments)} timed out."));
        }

        var standardOutput = await readOutput;
        var standardError = await readError;

        output.WriteLine(Redact(connectionString, $"dotnet {string.Join(' ', arguments)}"));
        output.WriteLine(Redact(connectionString, standardOutput));
        if (!string.IsNullOrWhiteSpace(standardError))
        {
            output.WriteLine(Redact(connectionString, standardError));
        }

        if (process.ExitCode != 0)
        {
            var environmentDump = string.Join(
                "; ",
                DebugEnvironmentVariableNames
                    .Select(name => name + "=" + Environment.GetEnvironmentVariable(name)));
            throw new InvalidOperationException(
                Redact(
                    connectionString,
                    $"dotnet {string.Join(' ', arguments)} exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}. ")
                + "env: " + environmentDump + " || "
                + Redact(connectionString, standardError.Length > 0 ? standardError : standardOutput));
        }
    }

    // dotnet-ef must match the locked EF Core version: the 10.0.3 tool mishandles build
    // output paths on Unix and fails its own build. The tool lives under artifacts/ so the
    // global tool installation stays untouched.
    private async Task<string> EnsureDotNetEfToolAsync(string repoRoot, string efCoreVersion)
    {
        var toolDirectory = Path.Combine(repoRoot, "artifacts", "dotnet-ef-tool");
        var toolPath = Path.Combine(toolDirectory, "dotnet-ef");

        if (File.Exists(toolPath))
        {
            var installed = await ReadToolVersionAsync(toolPath);
            if (string.Equals(installed, efCoreVersion, StringComparison.Ordinal))
            {
                return toolPath;
            }
        }

        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet))
        {
            dotnet = "dotnet";
        }

        Directory.CreateDirectory(toolDirectory);
        var action = File.Exists(toolPath) ? "update" : "install";
        await RunDotNetAsync(
            dotnet,
            repoRoot,
            DamengTestEnvironment.GetRequiredConnectionString(),
            [
                "tool", action, "dotnet-ef",
                "--version", efCoreVersion,
                "--tool-path", toolDirectory
            ]);

        var version = await ReadToolVersionAsync(toolPath);
        Assert.True(
            string.Equals(version, efCoreVersion, StringComparison.Ordinal),
            $"dotnet-ef {efCoreVersion} could not be provisioned under artifacts/dotnet-ef-tool.");
        output.WriteLine("dotnet-ef provisioned: " + version);
        return toolPath;
    }

    private static async Task<string?> ReadToolVersionAsync(string toolPath)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = toolPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    ArgumentList = { "--version" }
                });
            var toolOutput = await process!.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                return null;
            }

            var versionLine = toolOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Trim().Length > 0 && char.IsDigit(line.Trim()[0]));
            return versionLine?.Trim();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return null;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "W.EntityFrameworkCore.Dameng.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static (string EfCoreVersion, string DmProviderVersion) ResolveLockedPackageVersions(
        string repoRoot)
    {
        var props = File.ReadAllText(Path.Combine(repoRoot, "Directory.Packages.props"));
        var efCoreMatch = Regex.Match(
            props,
            "Microsoft\\.EntityFrameworkCore\\.Relational\"\\s+Version=\"\\[(?<version>[^,\\]]+)");
        var dmProviderMatch = Regex.Match(
            props,
            "DM\\.DmProvider\"\\s+Version=\"\\[(?<version>[^,\\]]+)");
        Assert.True(efCoreMatch.Success, "Directory.Packages.props must pin the EF Core range.");
        Assert.True(dmProviderMatch.Success, "Directory.Packages.props must pin the DM.DmProvider range.");
        return (efCoreMatch.Groups["version"].Value, dmProviderMatch.Groups["version"].Value);
    }

    private static string Redact(string connectionString, string text)
    {
        var redacted = text.Replace(connectionString, "[redacted]", StringComparison.OrdinalIgnoreCase);
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (string key in builder.Keys)
        {
            if (!key.Contains("password", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("pwd", StringComparison.OrdinalIgnoreCase)
                && !key.Contains("user", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("uid", StringComparison.OrdinalIgnoreCase)
                && !key.Contains("server", StringComparison.OrdinalIgnoreCase)
                && !key.Contains("host", StringComparison.OrdinalIgnoreCase)
                && !key.Contains("data source", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Convert.ToString(builder[key], CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(value))
            {
                redacted = redacted.Replace(value, "[redacted]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return redacted;
    }

    private static async Task<string?> ScalarStringAsync(
        DmConnection connection,
        string sql,
        string? parameterValue)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameterValue is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "name";
            parameter.Value = parameterValue;
            command.Parameters.Add(parameter);
        }

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task DropIfExistsAsync(
        DmConnection connection,
        string catalogView,
        string nameColumn,
        string objectName)
    {
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM {catalogView} WHERE {nameColumn} = :name";
            var parameter = count.CreateParameter();
            parameter.ParameterName = "name";
            parameter.Value = objectName;
            count.Parameters.Add(parameter);
            if (Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0L)
            {
                return;
            }
        }

        var objectType = catalogView.Contains("SEQUENCE", StringComparison.Ordinal) ? "SEQUENCE"
            : catalogView.Contains("VIEW", StringComparison.Ordinal) ? "VIEW" : "TABLE";
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP {objectType} \"{objectName}\"";
        await drop.ExecuteNonQueryAsync();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
