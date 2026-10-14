#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using W.Dm;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;
using W.EntityFrameworkCore.Dameng.WDriver.Design.Internal;
using W.EntityFrameworkCore.Dameng.WDriver.Infrastructure.Internal;
using Xunit;
using OfficialDmConnection = global::Dm.DmConnection;

namespace W.EntityFrameworkCore.Dameng.WDriver.Tests;

public sealed class WDriverConfigurationTests
{
    [Fact]
    public void SharedVisitorPreservesBoxedDateTimeTranslationBoundary()
    {
        using var context = new BlogContext(new DbContextOptionsBuilder<BlogContext>()
            .UseDamengWithWDriver(ConnectionString).Options);
#pragma warning disable CA1305 // Exercise the no-culture overload and boxed DateTime boundary.
        var error = Assert.Throws<InvalidOperationException>(() => context.Blogs
            .GroupBy(blog => ((object?)blog.OptionalCreated)!.ToString())
            .Select(group => group.Count()).ToQueryString());
        Assert.Contains("boxed DateTime.ToString", error.Message, StringComparison.Ordinal);
        var directNullable = context.Blogs.GroupBy(blog => blog.OptionalCreated.ToString())
            .Select(group => group.Count()).ToQueryString();
#pragma warning restore CA1305
        Assert.Contains("COALESCE(CAST(", directNullable, StringComparison.Ordinal);
    }

    internal const string ConnectionString
        = "Server=localhost;Port=5236;User=test;Password=SecretSentinel42;transport_security=PlaintextAllowed";

    private const string OfficialConnectionString = "Server=localhost;Port=5236;User=test;Password=test";

    [Fact]
    public void UseDamengWithWDriverRegistersTheWProviderAndConnection()
    {
        using var context = new BlogContext(WOptions<BlogContext>());

        Assert.Equal("W.EntityFrameworkCore.Dameng.WDriver", context.Database.ProviderName);
        Assert.IsType<DmConnection>(context.Database.GetDbConnection());
        Assert.Same(DamengWDriver.Instance, context.GetService<DamengDriver>());
        Assert.Equal(128, context.Model.GetMaxIdentifierLength());
    }

    [Fact]
    public void LogFragmentAndDebugInfoIdentifyTheWDriverWithoutSecrets()
    {
        var extension = WOptions<BlogContext>().FindExtension<DamengOptionsExtension>()!;
        var debugInfo = new Dictionary<string, string>();
        extension.Info.PopulateDebugInfo(debugInfo);

        Assert.Contains("UsingDameng Driver=W ", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.Equal("W", debugInfo["Dameng:Driver"]);
        Assert.DoesNotContain("SecretSentinel42", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.DoesNotContain(debugInfo.Values, value => value.Contains("SecretSentinel42", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryUseDamengWithWDriverOverloadSelectsTheWDriver()
    {
        using var connection = new DmConnection(ConnectionString);

        var untyped = new DbContextOptionsBuilder();
        var options = new List<DbContextOptions>
        {
            new DbContextOptionsBuilder().UseDamengWithWDriver(ConnectionString).Options,
            new DbContextOptionsBuilder().UseDamengWithWDriver(ConnectionString, dameng => dameng.CommandTimeout(5)).Options,
            new DbContextOptionsBuilder().UseDamengWithWDriver(connection).Options,
            new DbContextOptionsBuilder().UseDamengWithWDriver(connection, dameng => dameng.CommandTimeout(5)).Options,
            new DbContextOptionsBuilder().UseDamengWithWDriver(connection, contextOwnsConnection: false).Options,
            new DbContextOptionsBuilder<BlogContext>().UseDamengWithWDriver(ConnectionString).Options,
            new DbContextOptionsBuilder<BlogContext>().UseDamengWithWDriver(connection).Options,
            new DbContextOptionsBuilder<BlogContext>().UseDamengWithWDriver(connection, contextOwnsConnection: false).Options
        };

        foreach (var option in options)
        {
            Assert.Same(DamengWDriver.Instance, option.FindExtension<DamengOptionsExtension>()!.Driver);
        }

        Assert.Same(connection, options[2].FindExtension<DamengOptionsExtension>()!.Connection);
        Assert.Equal(5, options[3].FindExtension<DamengOptionsExtension>()!.CommandTimeout);
    }

    [Fact]
    public void ConnectionsAreCheckedAgainstTheSelectedDriver()
    {
        using var officialConnection = new OfficialDmConnection();
        using var wConnection = new DmConnection();
        using var sqlConnection = new SqlConnection();

        var fromW = Assert.Throws<ArgumentException>(
            () => new DbContextOptionsBuilder().UseDamengWithWDriver(officialConnection));
        var fromOfficial = Assert.Throws<ArgumentException>(
            () => new DbContextOptionsBuilder().UseDameng(wConnection));
        var fromSql = Assert.Throws<ArgumentException>(
            () => new DbContextOptionsBuilder().UseDamengWithWDriver(sqlConnection, contextOwnsConnection: true));

        foreach (var error in new[] { fromW, fromOfficial, fromSql })
        {
            Assert.Equal("Dameng:ForeignConnection", error.Data[DamengDriverErrors.ErrorIdKey]);
        }
    }

    [Fact]
    public void MixingDriversOnOneBuilderThrowsInEitherOrder()
    {
        var officialFirst = Assert.Throws<InvalidOperationException>(
            () => new DbContextOptionsBuilder()
                .UseDameng(OfficialConnectionString)
                .UseDamengWithWDriver(ConnectionString));
        var wFirst = Assert.Throws<InvalidOperationException>(
            () => new DbContextOptionsBuilder()
                .UseDamengWithWDriver(ConnectionString)
                .UseDameng(OfficialConnectionString));

        Assert.Equal("Dameng:ConflictingDriver", officialFirst.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.Equal("Dameng:ConflictingDriver", wFirst.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void BothDriversAndSqlServerCoexistInOneProcess()
    {
        for (var round = 0; round < 2; round++)
        {
            using var official = new BlogContext(
                new DbContextOptionsBuilder<BlogContext>().UseDameng(OfficialConnectionString).Options);
            using var w = new BlogContext(WOptions<BlogContext>());
            using var sqlServer = new BlogContext(
                new DbContextOptionsBuilder<BlogContext>()
                    .UseSqlServer("Server=localhost;Database=test;Trusted_Connection=True")
                    .Options);

            Assert.IsType<OfficialDmConnection>(official.Database.GetDbConnection());
            Assert.IsType<DmConnection>(w.Database.GetDbConnection());
            Assert.IsType<SqlConnection>(sqlServer.Database.GetDbConnection());
            Assert.NotSame(official.GetService<IRelationalTypeMappingSource>(), w.GetService<IRelationalTypeMappingSource>());
            Assert.Same(DamengOfficialDriver.Instance, official.GetService<DamengDriver>());
            Assert.Same(DamengWDriver.Instance, w.GetService<DamengDriver>());
        }
    }

    [Fact]
    public void BothDriversGenerateTheSameSql()
    {
        using var official = new BlogContext(
            new DbContextOptionsBuilder<BlogContext>().UseDameng(OfficialConnectionString).Options);
        using var w = new BlogContext(WOptions<BlogContext>());

        static string Query(BlogContext context)
            => context.Blogs
                .Where(blog => blog.Title.Contains("达梦") && blog.Created > new DateTime(2026, 1, 1))
                .OrderBy(blog => blog.Id)
                .Skip(1)
                .Take(2)
                .ToQueryString();

        Assert.Equal(Query(official), Query(w));
        Assert.Contains(":p", Query(w), StringComparison.Ordinal);
        Assert.DoesNotContain("@", Query(w), StringComparison.Ordinal);
        Assert.Equal(official.Database.GenerateCreateScript(), w.Database.GenerateCreateScript());
    }

    [Fact]
    public void SwitchingDriversProducesNoMigrationOperations()
    {
        using var official = new BlogContext(
            new DbContextOptionsBuilder<BlogContext>().UseDameng(OfficialConnectionString).Options);
        using var w = new BlogContext(WOptions<BlogContext>());
        var differ = w.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsModelDiffer>();

        var officialModel = official.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var wModel = w.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        Assert.Empty(differ.GetDifferences(officialModel, wModel));
        Assert.Empty(differ.GetDifferences(wModel, officialModel));
    }

    public static TheoryData<string> RetryOverloads
        => ["none", "count", "empty-collection", "collection", "full", "full-null"];

    [Theory]
    [MemberData(nameof(RetryOverloads))]
    public void EveryRetryOverloadIsRejected(string overload)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => new DbContextOptionsBuilder().UseDamengWithWDriver(
                ConnectionString,
                dameng => EnableRetry(dameng, overload)));

        AssertRetryRejected(error);
    }

    [Fact]
    public void RetryConfiguredBeforeSelectingTheWDriverIsRejected()
    {
        var builder = new DbContextOptionsBuilder();
        new DamengDbContextOptionsBuilder(builder).EnableRetryOnFailure(3);

        AssertRetryRejected(Assert.Throws<NotSupportedException>(() => builder.UseDamengWithWDriver(ConnectionString)));
    }

    [Fact]
    public void DirectlyConstructedRetryingStrategyIsRejected()
    {
        using var context = new BlogContext(
            WOptions<BlogContext>(
                dameng => dameng.ExecutionStrategy(
                    dependencies => new DamengRetryingExecutionStrategy(dependencies, 3))));

        AssertRetryRejected(Assert.Throws<NotSupportedException>(() => context.Database.CreateExecutionStrategy()));
        AssertRetryRejected(Assert.Throws<NotSupportedException>(() => new DamengRetryingExecutionStrategy(context)));
    }

    [Fact]
    public void DefaultExecutionStrategyRunsOperationsOnce()
    {
        using var context = new BlogContext(WOptions<BlogContext>());
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        Assert.False(strategy.RetriesOnFailure);
        Assert.Throws<TimeoutException>(() => strategy.Execute(() =>
        {
            attempts++;
            throw new TimeoutException();
        }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void CustomExecutionStrategyRemainsAnExplicitExtensionPoint()
    {
        using var context = new BlogContext(
            WOptions<BlogContext>(dameng => dameng.ExecutionStrategy(dependencies => new CountingStrategy(dependencies))));

        Assert.IsType<CountingStrategy>(context.Database.CreateExecutionStrategy());
    }

    [Fact]
    public void WServicesAcceptWOptionsAndRejectOfficialOptions()
    {
        var services = new ServiceCollection().AddEntityFrameworkDamengWithWDriver().BuildServiceProvider();
        var wOptions = new DbContextOptionsBuilder<BlogContext>()
            .UseInternalServiceProvider(services)
            .UseDamengWithWDriver(ConnectionString)
            .Options;
        var officialOptions = new DbContextOptionsBuilder<BlogContext>()
            .UseInternalServiceProvider(services)
            .UseDameng(OfficialConnectionString)
            .Options;

        using (var w = new BlogContext(wOptions))
        {
            Assert.NotNull(w.Model);
            Assert.Equal("W.EntityFrameworkCore.Dameng.WDriver", w.Database.ProviderName);
        }

        using var official = new BlogContext(officialOptions);
        var error = Assert.Throws<InvalidOperationException>(() => _ = official.Model);
        Assert.Equal("Dameng:MismatchedDriverServices", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void OfficialServicesRejectWOptions()
    {
        var services = new ServiceCollection().AddEntityFrameworkDameng().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<BlogContext>()
            .UseInternalServiceProvider(services)
            .UseDamengWithWDriver(ConnectionString)
            .Options;

        using var context = new BlogContext(options);
        var error = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Equal("Dameng:MismatchedDriverServices", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisteringBothDriversInOneProviderFails(bool officialFirst)
    {
        var collection = new ServiceCollection();
        if (officialFirst)
        {
            collection.AddEntityFrameworkDameng().AddEntityFrameworkDamengWithWDriver();
        }
        else
        {
            collection.AddEntityFrameworkDamengWithWDriver().AddEntityFrameworkDameng();
        }

        var options = new DbContextOptionsBuilder<BlogContext>()
            .UseInternalServiceProvider(collection.BuildServiceProvider())
            .UseDamengWithWDriver(ConnectionString)
            .Options;
        using var context = new BlogContext(options);

        var error = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Equal("Dameng:ConflictingDriverServices", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void DesignTimeProviderServicesAreDiscoveredFromTheProviderName()
    {
        using var context = new BlogContext(WOptions<BlogContext>());
        var providerAssembly = Assembly.Load(new AssemblyName(context.Database.ProviderName!));
        var attribute = providerAssembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(DamengWDriverDesignTimeServices), providerAssembly.GetType(attribute.TypeName));
    }

    [Fact]
    public void DesignTimeServicesUseTheWDriver()
    {
        var services = new ServiceCollection();
        new DamengWDriverDesignTimeServices().ConfigureDesignTimeServices(services);
        using var provider = services.BuildServiceProvider();

        var call = provider.GetRequiredService<IProviderConfigurationCodeGenerator>()
            .GenerateUseProvider(ConnectionString, providerOptions: null);
        using var officialConnection = new OfficialDmConnection();
        var error = Assert.Throws<ArgumentException>(
            () => provider.GetRequiredService<IDatabaseModelFactory>()
                .Create(officialConnection, new DatabaseModelFactoryOptions()));

        Assert.Same(DamengWDriver.Instance, provider.GetRequiredService<DamengDriver>());
        Assert.Equal(nameof(DamengWDriverDbContextOptionsBuilderExtensions.UseDamengWithWDriver), call.Method);
        Assert.Equal(nameof(DamengWDriverDbContextOptionsBuilderExtensions), call.DeclaringType);
        Assert.Equal("Dameng:ForeignConnection", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void LoadedDriverAssemblyMatchesThePinnedManifest()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "w-damengprovider.manifest.json")));
        var assembly = typeof(DmConnection).Assembly;

        Assert.Equal("W.DamengProvider", assembly.GetName().Name);
        Assert.Equal(
            manifest.RootElement.GetProperty("assemblyMvid").GetString(),
            assembly.ManifestModule.ModuleVersionId.ToString());
        Assert.Equal(
            manifest.RootElement.GetProperty("assemblySha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            loaded => loaded.GetName().Name == "W.DmProvider");
        Assert.Equal(DmDiagnostics.ActivitySourceName, manifest.RootElement.GetProperty("diagnosticsSourceName").GetString());
    }

    internal static DbContextOptions<TContext> WOptions<TContext>(
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseDamengWithWDriver(ConnectionString, damengOptionsAction)
            .Options;

    private static void EnableRetry(DamengDbContextOptionsBuilder dameng, string overload)
    {
        switch (overload)
        {
            case "none":
                dameng.EnableRetryOnFailure();
                break;
            case "count":
                dameng.EnableRetryOnFailure(2);
                break;
            case "empty-collection":
                dameng.EnableRetryOnFailure(Array.Empty<int>());
                break;
            case "collection":
                dameng.EnableRetryOnFailure(new[] { -6010 });
                break;
            case "full":
                dameng.EnableRetryOnFailure(2, TimeSpan.FromSeconds(1), new[] { -6010 });
                break;
            case "full-null":
                dameng.EnableRetryOnFailure(2, TimeSpan.FromSeconds(1), null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overload));
        }
    }

    private static void AssertRetryRejected(NotSupportedException error)
    {
        Assert.Equal("Dameng:RetryNotSupportedForWDriver", error.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.StartsWith("Dameng:RetryNotSupportedForWDriver", error.Message, StringComparison.Ordinal);
    }

    private sealed class CountingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception)
            => false;
    }
}

internal sealed class BlogContext(DbContextOptions<BlogContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs
        => Set<Blog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Blog>(
            entity =>
            {
                entity.Property(blog => blog.Title).HasMaxLength(200);
                entity.Property(blog => blog.Body);
                entity.Property(blog => blog.Image);
                entity.Property(blog => blog.Short).HasMaxLength(16);
                entity.Property(blog => blog.Precise).HasPrecision(3);
            });
}

internal sealed class Blog
{
    public long Id { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public byte[]? Image { get; set; }

    public byte[]? Short { get; set; }

    public DateTime Created { get; set; }

    public DateTime? OptionalCreated { get; set; }

    public DateTime Precise { get; set; }

    public DateTimeOffset Published { get; set; }

    public TimeOnly At { get; set; }

    public TimeSpan Duration { get; set; }

    public DateOnly Day { get; set; }
}
