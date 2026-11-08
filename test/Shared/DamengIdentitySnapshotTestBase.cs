#pragma warning disable EF1001 // This fixture inspects provider-specific relational annotations.

using System.Text.Json;
using System.Xml.Linq;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;
using Xunit;
using Xunit.Abstractions;
using static W.EntityFrameworkCore.Dameng.FunctionalTests.DamengCliTestHarness;

namespace W.EntityFrameworkCore.Dameng.Tests;

// EnsureToolAsync shares the repository tool path with the existing CLI fixture tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IdentitySnapshotCliGroup
{
    public const string Name = "Dameng identity snapshot CLI";
}

public abstract class DamengIdentitySnapshotTestBase(ITestOutputHelper output)
{
    private protected abstract string ConfigureMethod { get; }
    private protected abstract string ProviderName { get; }
    private protected abstract AssemblyIdentity[] ProviderAssemblies { get; }
    private protected virtual AssemblyIdentity? PinnedDriver => null;

    [Theory]
    [Trait("Plan", "022")]
    [InlineData("Default", "", 1L, 1, null, null)]
    [InlineData("Inherited", "", 5L, 10, null, null)]
    [InlineData("SeedOnly", "id.Metadata.SetDamengIdentitySeed(7);", 7L, 10, 7L, null)]
    [InlineData("IncrementOnly", "id.Metadata.SetDamengIdentityIncrement(3);", 5L, 3, null, 3)]
    [InlineData("Both", "id.UseDamengIdentityColumn(7, 3);", 7L, 3, 7L, 3)]
    [InlineData("ExplicitDefaults", "id.UseDamengIdentityColumn();", 1L, 1, 1L, 1)]
    public async Task IdentitySnapshotPreservesEffectiveFacets(
        string scenario, string propertyConfiguration, long seed, int increment, long? propertySeed, int? propertyIncrement)
    {
        var repository = FindRepositoryRoot();
        var projectName = GetType().Assembly.GetName().Name!;
        using var lockFile = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repository, "test", projectName, "packages.lock.json")));
        var packages = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0");
        var efVersion = ResolvedVersion(packages, "Microsoft.EntityFrameworkCore.Relational");
        var providerAssemblies = ProviderAssemblies;
        var pinnedDriver = PinnedDriver;
        var loadedAssemblies = pinnedDriver is null ? providerAssemblies : [.. providerAssemblies, pinnedDriver];
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var dotnetEf = await EnsureToolAsync(dotnet, repository, efVersion, async start =>
        {
            var result = await RunAsync(start);
            output.WriteLine($"dotnet-ef version or installation check exit={result.ExitCode}");
            if (result.ExitCode != 0)
                output.WriteLine(result.SafeOutput);
            return result;
        });
        output.WriteLine($"dotnet-ef locked version verified: {efVersion}");

        var directory = CreatePrivateDirectory();
        try
        {
            var references = new XElement("ItemGroup",
                providerAssemblies.Select(identity => new XElement("Reference",
                    new XAttribute("Include", identity.Name), new XElement("HintPath", identity.Location))),
                PackageReference("Microsoft.EntityFrameworkCore.Design", efVersion),
                PackageReference("DM.DmProvider", ResolvedVersion(packages, "DM.DmProvider")));
            if (pinnedDriver is not null)
                references.Add(PackageReference("W.DamengProvider", ResolvedVersion(packages, "W.DamengProvider")));

            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("OutputType", "Exe"),
                    new XElement("TargetFramework", "net10.0"),
                    new XElement("Nullable", "enable"),
                    new XElement("ImplicitUsings", "enable")),
                references).Save(Path.Combine(directory, "IdentitySnapshot.csproj"));

            var useModelDefaults = scenario == "Default" ? "" : "modelBuilder.UseDamengIdentityColumns(5, 10);";
            File.WriteAllText(Path.Combine(directory, "IdentitySnapshotContext.cs"),
                ContextSource(ConfigureMethod, useModelDefaults, propertyConfiguration, loadedAssemblies));
            File.WriteAllText(Path.Combine(directory, "Program.cs"),
                VerificationSource(ProviderName, seed, increment, propertySeed, propertyIncrement, scenario != "Default"));

            var buildOutput = Path.Combine(directory, "bin", "Debug", "net10.0");
            var consumer = Path.Combine(buildOutput, "IdentitySnapshot.dll");
            string[] singleNode = ["-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false"];
            async Task BuildAsync(string stage)
            {
                await RunStepAsync(stage, dotnet, ["build", "--no-restore", "--disable-build-servers", .. singleNode]);
                VerifyNestedCopies(loadedAssemblies, buildOutput);
            }

            await RunStepAsync("restore", dotnet, ["restore", "--disable-parallel", .. singleNode]);
            await BuildAsync("build initial context");
            await RunStepAsync("verify initial context", dotnet, [consumer, "context"]);

            await RunStepAsync("scaffold InitialIdentity", dotnetEf,
                ["migrations", "add", "InitialIdentity", "--context", "IdentitySnapshotContext", "--no-build"]);
            await BuildAsync("compile InitialIdentity snapshot");
            await RunStepAsync("compare InitialIdentity snapshot", dotnet, [consumer, "initial"]);

            await RunStepAsync("scaffold NoChanges", dotnetEf,
                ["migrations", "add", "NoChanges", "--context", "IdentitySnapshotContext", "--no-build"]);
            await BuildAsync("compile NoChanges migration and snapshot");
            await RunStepAsync("compare NoChanges migration and snapshot", dotnet, [consumer, "second"]);

            async Task RunStepAsync(string stage, string executable, string[] arguments)
            {
                var result = await RunAsync(CreateProcess(executable, directory, arguments, connection: null));
                output.WriteLine($"{stage}: exit={result.ExitCode}");
                output.WriteLine(result.SafeOutput);
                Assert.True(result.ExitCode == 0, $"{stage} failed (exit={result.ExitCode}).\n{result.SafeOutput}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            output.WriteLine("Private consumer directory deleted: " + Path.GetFileName(directory));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "W.EntityFrameworkCore.Dameng.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string ResolvedVersion(JsonElement packages, string name)
        => packages.GetProperty(name).GetProperty("resolved").GetString()!;

    private static XElement PackageReference(string package, string version)
        => new("PackageReference", new XAttribute("Include", package), new XAttribute("Version", $"[{version}]"));

    private static string ContextSource(
        string configureMethod, string modelConfiguration, string propertyConfiguration, AssemblyIdentity[] identities)
        => $$"""
            using System.Data.Common;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Design;
            using Microsoft.EntityFrameworkCore.Diagnostics;

            public sealed class IdentitySnapshotContext : DbContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder options)
                    => options.{{configureMethod}}((string?)null).AddInterceptors(new RejectConnection());

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    {{modelConfiguration}}
                    var entity = modelBuilder.Entity<IdentityRow>().ToTable("IDENTITY_SNAPSHOT");
                    var id = entity.Property(row => row.Id).HasColumnName("ID");
                    {{propertyConfiguration}}
                }

                private sealed class RejectConnection : DbConnectionInterceptor
                {
                    public override InterceptionResult ConnectionOpening(
                        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                        => throw new InvalidOperationException("Identity snapshot fixture must remain offline.");

                    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
                        CancellationToken cancellationToken = default)
                        => throw new InvalidOperationException("Identity snapshot fixture must remain offline.");
                }
            }

            public sealed class IdentityRow
            {
                public long Id { get; set; }
            }

            public sealed class IdentitySnapshotFactory : IDesignTimeDbContextFactory<IdentitySnapshotContext>
            {
                public IdentitySnapshotContext CreateDbContext(string[] args)
                {
                    {{IdentityVerificationSource(identities)}}
                    return new IdentitySnapshotContext();
                }
            }
            """;

    private static string VerificationSource(
        string providerName, long seed, int increment, long? propertySeed, int? propertyIncrement, bool hasModelDefaults)
        => FormattableString.Invariant($$"""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Microsoft.EntityFrameworkCore.Metadata;
            using Microsoft.EntityFrameworkCore.Migrations;

            using var context = new IdentitySnapshotFactory().CreateDbContext([]);
            Require(context.Database.ProviderName == "{{providerName}}", "Unexpected active provider.");
            var source = context.GetService<IDesignTimeModel>().Model;
            var sourceProperty = GetMappedProperty(source);
            Require(Equals(sourceProperty["{{DamengAnnotationNames.ValueGenerationStrategy}}"], DamengValueGenerationStrategy.IdentityColumn),
                "Expected a source property IdentityColumn strategy annotation.");
            Require(Equals(sourceProperty["{{DamengAnnotationNames.IdentitySeed}}"], {{(propertySeed is null ? "null" : $"{propertySeed}L")}}),
                "Unexpected explicit source property seed.");
            Require(Equals(sourceProperty["{{DamengAnnotationNames.IdentityIncrement}}"], {{(propertyIncrement?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}}),
                "Unexpected explicit source property increment.");
            Require(Equals(source["{{DamengAnnotationNames.IdentitySeed}}"], {{(hasModelDefaults ? "5L" : "null")}}),
                "Unexpected source model seed annotation.");
            Require(Equals(source["{{DamengAnnotationNames.IdentityIncrement}}"], {{(hasModelDefaults ? "10" : "null")}}),
                "Unexpected source model increment annotation.");
            var expected = (DamengValueGenerationStrategy.IdentityColumn, {{seed}}L, {{increment}});
            var sourceFacets = ReadFacets(source, sourceProperty);
            Require(sourceFacets == expected, $"Source effective facets were {sourceFacets}, expected {expected}.");
            if (args[0] == "context")
                return;

            var migrations = context.GetService<IMigrationsAssembly>();
            var snapshot = migrations.ModelSnapshot
                ?? throw new InvalidOperationException("No compiled model snapshot was found.");
            var snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
            Require(!ReferenceEquals(source, snapshotModel), "Source and snapshot must be distinct models.");
            var snapshotFacets = ReadFacets(snapshotModel, GetMappedProperty(snapshotModel));
            var differences = context.GetService<IMigrationsModelDiffer>()
                .GetDifferences(snapshotModel.GetRelationalModel(), source.GetRelationalModel());
            Require(snapshotFacets == sourceFacets,
                $"Source effective facets {sourceFacets}; snapshot effective facets {snapshotFacets}; model differences: {differences.Count}.");
            Require(differences.Count == 0, $"Expected zero model differences, got {differences.Count}.");
            Console.WriteLine($"Compiled snapshot effective facets {snapshotFacets}; model differences: {differences.Count}.");

            if (args[0] == "second")
            {
                var migration = migrations.Migrations.Single(pair => pair.Key.EndsWith("_NoChanges", StringComparison.Ordinal));
                var instance = migrations.CreateMigration(migration.Value, context.Database.ProviderName!);
                Require(instance.UpOperations.Count == 0 && instance.DownOperations.Count == 0,
                    $"NoChanges operations: Up={instance.UpOperations.Count}, Down={instance.DownOperations.Count}.");
                Console.WriteLine("Compiled NoChanges migration: Up=0, Down=0.");
            }

            static IProperty GetMappedProperty(IModel model)
            {
                var entity = model.FindEntityType(typeof(IdentityRow).FullName!)
                    ?? throw new InvalidOperationException("Identity entity missing.");
                Require(entity.GetTableName() == "IDENTITY_SNAPSHOT", "Identity entity table name changed.");
                var property = entity.FindProperty(nameof(IdentityRow.Id))
                    ?? throw new InvalidOperationException("Identity property missing.");
                Require(property.GetColumnName(StoreObjectIdentifier.Table("IDENTITY_SNAPSHOT", null)) == "ID",
                    "Identity property column name changed.");
                return property;
            }

            static (DamengValueGenerationStrategy Strategy, long Seed, int Increment) ReadFacets(IModel model, IProperty property)
            {
                var table = model.GetRelationalModel().FindTable("IDENTITY_SNAPSHOT", null)
                    ?? throw new InvalidOperationException("Relational table missing.");
                var column = table.FindColumn("ID")
                    ?? throw new InvalidOperationException("Relational identity column missing.");
                Require(column.PropertyMappings.Any(mapping => ReferenceEquals(mapping.Property, property)),
                    "Relational column must map to the identity property.");

                var facets = (property.GetDamengValueGenerationStrategy(StoreObjectIdentifier.Table(table.Name, table.Schema)),
                    property.GetDamengIdentitySeed(), property.GetDamengIdentityIncrement());
                Require(Equals(column["{{DamengAnnotationNames.ValueGenerationStrategy}}"], facets.Item1)
                    && Equals(column["{{DamengAnnotationNames.IdentitySeed}}"], facets.Item2)
                    && Equals(column["{{DamengAnnotationNames.IdentityIncrement}}"], facets.Item3),
                    $"Relational column annotations did not match effective facets {facets}.");
                return facets;
            }

            static void Require(bool condition, string message)
            {
                if (!condition)
                    throw new InvalidOperationException(message);
            }
            """);
}
