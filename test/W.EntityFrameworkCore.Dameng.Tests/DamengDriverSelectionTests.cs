#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

using System.Data;
using System.Data.Common;
using System.Reflection;
using Dm;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDriverSelectionTests
{
    private const string ConnectionString = "Server=localhost;Port=5236;User=test;Password=SecretSentinel42";

    [Fact]
    public void OfficialOptionsIdentifyTheDriverInLogsAndDebugInfoWithoutSecrets()
    {
        var extension = CreateOfficialOptions().FindExtension<DamengOptionsExtension>()!;
        var debugInfo = new Dictionary<string, string>();
        extension.Info.PopulateDebugInfo(debugInfo);

        Assert.Same(DamengOfficialDriver.Instance, extension.Driver);
        Assert.Contains("UsingDameng Driver=Official ", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.Equal("Official", debugInfo["Dameng:Driver"]);
        Assert.DoesNotContain("SecretSentinel42", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.DoesNotContain(debugInfo.Values, value => value.Contains("SecretSentinel42", StringComparison.Ordinal));
    }

    [Fact]
    public void FakeDriverOptionsUseTheirOwnNameInLogsAndDebugInfo()
    {
        var extension = CreateFakeOptions().FindExtension<DamengOptionsExtension>()!;
        var debugInfo = new Dictionary<string, string>();
        extension.Info.PopulateDebugInfo(debugInfo);

        Assert.Contains("UsingDameng Driver=Fake ", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.Equal("Fake", debugInfo["Dameng:Driver"]);
        Assert.DoesNotContain("SecretSentinel42", extension.Info.LogFragment, StringComparison.Ordinal);
    }

    [Fact]
    public void DriversDoNotShareInternalServiceProviders()
    {
        var official = CreateOfficialOptions().FindExtension<DamengOptionsExtension>()!.Info;
        var officialOther = new DbContextOptionsBuilder()
            .UseDameng("Server=other;Port=5236;User=other;Password=other")
            .Options.FindExtension<DamengOptionsExtension>()!.Info;
        var fake = CreateFakeOptions().FindExtension<DamengOptionsExtension>()!.Info;

        Assert.True(official.ShouldUseSameServiceProvider(officialOther));
        Assert.Equal(official.GetServiceProviderHashCode(), officialOther.GetServiceProviderHashCode());
        Assert.False(official.ShouldUseSameServiceProvider(fake));
        Assert.False(fake.ShouldUseSameServiceProvider(official));
    }

    [Fact]
    public void SameContextTypeAlternatesBetweenDriversWithoutSharingServices()
    {
        for (var round = 0; round < 2; round++)
        {
            using var officialContext = new MappingContext(CreateOfficialOptions<MappingContext>());
            using var fakeContext = new MappingContext(CreateFakeOptions<MappingContext>());

            Assert.Same(DamengOfficialDriver.Instance, officialContext.GetService<DamengDriver>());
            Assert.Same(FakeDamengDriver.Instance, fakeContext.GetService<DamengDriver>());
            Assert.NotSame(
                officialContext.GetService<IRelationalTypeMappingSource>(),
                fakeContext.GetService<IRelationalTypeMappingSource>());
            Assert.Equal("W.EntityFrameworkCore.Dameng", officialContext.Database.ProviderName);
            Assert.Equal(FakeDamengDriver.Instance.ProviderName, fakeContext.Database.ProviderName);
            Assert.IsType<DmConnection>(officialContext.Database.GetDbConnection());
            Assert.IsType<SqlConnection>(fakeContext.Database.GetDbConnection());
            Assert.Same(
                DamengOfficialDriver.Instance,
                GetBoundDriver(officialContext, nameof(MappingEntity.Text)));
            Assert.Same(
                FakeDamengDriver.Instance,
                GetBoundDriver(fakeContext, nameof(MappingEntity.Text)));
        }
    }

    [Fact]
    public void PooledContextsKeepTheirDriver()
    {
        var services = new ServiceCollection()
            .AddDbContextPool<MappingContext>(options => options.UseDameng(ConnectionString))
            .BuildServiceProvider();
        var fakeServices = new ServiceCollection()
            .AddDbContextPool<MappingContext>(
                options => DamengDriverConfiguration.UseDriver(options, FakeDamengDriver.Instance, ConnectionString, null))
            .BuildServiceProvider();

        for (var round = 0; round < 2; round++)
        {
            using var officialScope = services.CreateScope();
            using var fakeScope = fakeServices.CreateScope();
            var officialContext = officialScope.ServiceProvider.GetRequiredService<MappingContext>();
            var fakeContext = fakeScope.ServiceProvider.GetRequiredService<MappingContext>();

            Assert.Same(DamengOfficialDriver.Instance, officialContext.GetService<DamengDriver>());
            Assert.Same(FakeDamengDriver.Instance, fakeContext.GetService<DamengDriver>());
        }
    }

    [Fact]
    public void UseDamengRejectsConnectionsOfAnotherDriver()
    {
        using var connection = new SqlConnection();

        var error = Assert.Throws<ArgumentException>(
            () => new DbContextOptionsBuilder().UseDameng(connection));

        Assert.Equal("Dameng:ForeignConnection", error.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.Contains(typeof(SqlConnection).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverConnectionOverloadRejectsConnectionsOfAnotherDriver()
    {
        using var connection = new DmConnection();

        var error = Assert.Throws<ArgumentException>(
            () => DamengDriverConfiguration.UseDriver(
                new DbContextOptionsBuilder(),
                FakeDamengDriver.Instance,
                connection,
                contextOwnsConnection: false,
                damengOptionsAction: null));

        Assert.Equal("Dameng:ForeignConnection", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void SelectingASecondDriverOnTheSameBuilderThrows()
    {
        var officialFirst = new DbContextOptionsBuilder().UseDameng(ConnectionString);
        var officialError = Assert.Throws<InvalidOperationException>(
            () => DamengDriverConfiguration.UseDriver(officialFirst, FakeDamengDriver.Instance, ConnectionString, null));

        var fakeFirst = DamengDriverConfiguration.UseDriver(
            new DbContextOptionsBuilder(),
            FakeDamengDriver.Instance,
            ConnectionString,
            null);
        var fakeError = Assert.Throws<InvalidOperationException>(() => fakeFirst.UseDameng(ConnectionString));

        Assert.Equal("Dameng:ConflictingDriver", officialError.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.Equal("Dameng:ConflictingDriver", fakeError.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void RepeatingTheSameDriverKeepsExistingSemantics()
    {
        var builder = new DbContextOptionsBuilder()
            .UseDameng("Server=first;Port=5236;User=test;Password=test")
            .UseDameng(ConnectionString, dameng => dameng.CommandTimeout(23));

        var extension = builder.Options.FindExtension<DamengOptionsExtension>()!;
        Assert.Equal(ConnectionString, extension.ConnectionString);
        Assert.Equal(23, extension.CommandTimeout);
        Assert.Same(DamengOfficialDriver.Instance, extension.Driver);
    }

    [Fact]
    public void OfficialDriverStillSupportsBuiltInRetries()
    {
        using var context = new MappingContext(
            CreateOfficialOptions<MappingContext>(dameng => dameng.EnableRetryOnFailure(3)));

        Assert.IsType<DamengRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
        Assert.True(context.GetService<IDbContextOptions>().FindExtension<DamengOptionsExtension>()!.RetryOnFailureRequested);
    }

    public static TheoryData<string> RetryOverloads
        => ["none", "count", "empty-collection", "collection", "full", "full-null"];

    [Theory]
    [MemberData(nameof(RetryOverloads))]
    public void EveryRetryOverloadIsRejectedForADriverWithoutRetries(string overload)
    {
        var error = Assert.Throws<NotSupportedException>(
            () => DamengDriverConfiguration.UseDriver(
                new DbContextOptionsBuilder(),
                FakeDamengDriver.Instance,
                ConnectionString,
                dameng => EnableRetry(dameng, overload)));

        AssertRetryRejected(error);
    }

    [Fact]
    public void RetryConfiguredBeforeSelectingTheDriverIsRejectedOnSelection()
    {
        var builder = new DbContextOptionsBuilder();
        new DamengDbContextOptionsBuilder(builder).EnableRetryOnFailure();

        var error = Assert.Throws<NotSupportedException>(
            () => DamengDriverConfiguration.UseDriver(builder, FakeDamengDriver.Instance, ConnectionString, null));

        AssertRetryRejected(error);
    }

    [Fact]
    public void InvalidRetryArgumentsStillThrowStandardArgumentExceptions()
        => Assert.Throws<ArgumentNullException>(
            () => DamengDriverConfiguration.UseDriver(
                new DbContextOptionsBuilder(),
                FakeDamengDriver.Instance,
                ConnectionString,
                dameng => dameng.EnableRetryOnFailure((ICollection<int>)null!)));

    [Fact]
    public void DirectlyConstructedRetryingStrategyIsRejectedForADriverWithoutRetries()
    {
        using var context = new MappingContext(
            CreateFakeOptions<MappingContext>(
                dameng => dameng.ExecutionStrategy(dependencies => new DamengRetryingExecutionStrategy(dependencies))));

        var error = Assert.Throws<NotSupportedException>(() => context.Database.CreateExecutionStrategy());

        AssertRetryRejected(error);
    }

    [Fact]
    public void RetryingStrategyConstructedFromAContextIsRejectedForADriverWithoutRetries()
    {
        using var context = new MappingContext(CreateFakeOptions<MappingContext>());

        var error = Assert.Throws<NotSupportedException>(() => new DamengRetryingExecutionStrategy(context));

        AssertRetryRejected(error);
    }

    [Fact]
    public void DefaultStrategyForADriverWithoutRetriesExecutesOnce()
    {
        using var context = new MappingContext(CreateFakeOptions<MappingContext>());
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        Assert.False(strategy.RetriesOnFailure);
        Assert.Throws<TimeoutException>(
            () => strategy.Execute(() =>
            {
                attempts++;
                throw new TimeoutException();
            }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void OfficialServicesRejectFakeDriverOptions()
    {
        var services = new ServiceCollection().AddEntityFrameworkDameng().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<MappingContext>()
            .UseInternalServiceProvider(services);
        DamengDriverConfiguration.UseDriver(options, FakeDamengDriver.Instance, ConnectionString, null);

        using var context = new MappingContext(options.Options);
        var error = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Equal("Dameng:MismatchedDriverServices", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void FakeServicesRejectOfficialDriverOptions()
    {
        var services = DamengDriverConfiguration
            .AddEntityFrameworkDameng(new ServiceCollection(), FakeDamengDriver.Instance)
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<MappingContext>()
            .UseInternalServiceProvider(services)
            .UseDameng(ConnectionString)
            .Options;

        using var context = new MappingContext(options);
        var error = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Equal("Dameng:MismatchedDriverServices", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void ReusedInternalServiceProviderRejectsOptionsOfAnotherDriver()
    {
        var services = new ServiceCollection().AddEntityFrameworkDameng().BuildServiceProvider();
        var officialOptions = new DbContextOptionsBuilder<MappingContext>()
            .UseInternalServiceProvider(services)
            .UseDameng(ConnectionString)
            .Options;
        using (var first = new MappingContext(officialOptions))
        {
            Assert.NotNull(first.Model);
        }

        var fakeOptions = new DbContextOptionsBuilder<MappingContext>().UseInternalServiceProvider(services);
        DamengDriverConfiguration.UseDriver(fakeOptions, FakeDamengDriver.Instance, ConnectionString, null);
        using var second = new MappingContext(fakeOptions.Options);

        var error = Assert.Throws<InvalidOperationException>(() => _ = second.Model);
        Assert.Equal("Dameng:MismatchedDriverServices", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisteringServicesForTwoDriversFailsInEitherOrder(bool officialFirst)
    {
        var collection = new ServiceCollection();
        if (officialFirst)
        {
            collection.AddEntityFrameworkDameng();
            DamengDriverConfiguration.AddEntityFrameworkDameng(collection, FakeDamengDriver.Instance);
        }
        else
        {
            DamengDriverConfiguration.AddEntityFrameworkDameng(collection, FakeDamengDriver.Instance);
            collection.AddEntityFrameworkDameng();
        }

        var services = collection.BuildServiceProvider();
        var officialOptions = new DbContextOptionsBuilder<MappingContext>()
            .UseInternalServiceProvider(services)
            .UseDameng(ConnectionString)
            .Options;
        var fakeBuilder = new DbContextOptionsBuilder<MappingContext>().UseInternalServiceProvider(services);
        DamengDriverConfiguration.UseDriver(fakeBuilder, FakeDamengDriver.Instance, ConnectionString, null);

        using var officialContext = new MappingContext(officialOptions);
        using var fakeContext = new MappingContext(fakeBuilder.Options);

        var officialError = Assert.Throws<InvalidOperationException>(() => _ = officialContext.Model);
        var fakeError = Assert.Throws<InvalidOperationException>(() => _ = fakeContext.Model);
        Assert.Equal("Dameng:ConflictingDriverServices", officialError.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.Equal("Dameng:ConflictingDriverServices", fakeError.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void RegisteringTheSameDriverTwiceIsAllowed()
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkDameng()
            .AddEntityFrameworkDameng()
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<MappingContext>()
            .UseInternalServiceProvider(services)
            .UseDameng(ConnectionString)
            .Options;

        using var context = new MappingContext(options);

        Assert.NotNull(context.Model);
        Assert.Equal("W.EntityFrameworkCore.Dameng", context.Database.ProviderName);
    }

    [Fact]
    public void ModelOfOneDriverCannotConfigureParametersOfAnother()
    {
        using var officialContext = new MappingContext(CreateOfficialOptions<MappingContext>());
        var clobMapping = officialContext.GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(string), null, unicode: true, size: 40000)!;
        using var foreignCommand = new SqlCommand();

        var error = Assert.Throws<InvalidOperationException>(
            () => clobMapping.CreateParameter(foreignCommand, "p", "text"));

        Assert.Equal("Dameng:ForeignParameter", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void BoundMappingsKeepTheirDriverAcrossClones()
    {
        using var context = new MappingContext(CreateFakeOptions<MappingContext>());
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mappings = new RelationalTypeMapping[]
        {
            source.FindMapping(typeof(string), null, unicode: true, size: 40000)!,
            source.FindMapping(typeof(byte[]), null, size: 16)!,
            source.FindMapping(typeof(TimeSpan))!,
            source.FindMapping(typeof(TimeOnly))!,
            source.FindMapping(typeof(DateTime))!,
            source.FindMapping(typeof(DateTimeOffset))!,
            source.FindMapping("TIMESTAMP")!,
            source.FindMapping(typeof(DateTime), "DATE")!
        };

        foreach (var mapping in mappings)
        {
            var bound = Assert.IsAssignableFrom<IDamengDriverBoundMapping>(mapping);
            Assert.Same(FakeDamengDriver.Instance, bound.Driver);
            Assert.Same(
                FakeDamengDriver.Instance,
                Assert.IsAssignableFrom<IDamengDriverBoundMapping>(mapping.WithPrecisionAndScale(3, null)).Driver);
            Assert.Same(
                FakeDamengDriver.Instance,
                Assert.IsAssignableFrom<IDamengDriverBoundMapping>(
                    mapping.WithStoreTypeAndSize(mapping.StoreType, mapping.Size)).Driver);
        }
    }

    [Fact]
    public void UnboundMappingsRefuseToConfigureParameters()
    {
        var mapping = new DamengStringTypeMapping("CLOB", DbType.AnsiString, unicode: false, size: null, fixedLength: false, lob: true);
        using var command = new DmCommand();

        var error = Assert.Throws<InvalidOperationException>(() => mapping.CreateParameter(command, "p", "text"));

        Assert.Equal("Dameng:UnboundTypeMapping", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void DriverSetsProviderTypesForEverySharedParameterKind()
    {
        using var context = new MappingContext(CreateFakeOptions<MappingContext>());
        var source = context.GetService<IRelationalTypeMappingSource>();
        using var command = new SqlCommand();

        Assert.Equal(
            SqlDbType.Text,
            ((SqlParameter)source.FindMapping(typeof(string), null, unicode: true, size: 40000)!
                .CreateParameter(command, "clob", "达梦")).SqlDbType);
        Assert.Equal(
            SqlDbType.Image,
            ((SqlParameter)source.FindMapping(typeof(byte[]), null, size: 40000)!
                .CreateParameter(command, "blob", new byte[] { 1 })).SqlDbType);
        Assert.Equal(
            SqlDbType.VarBinary,
            ((SqlParameter)source.FindMapping(typeof(byte[]), null, size: 16)!
                .CreateParameter(command, "varbinary", new byte[] { 1 })).SqlDbType);
        Assert.Equal(
            SqlDbType.Binary,
            ((SqlParameter)source.FindMapping(typeof(byte[]), null, fixedLength: true, size: 16)!
                .CreateParameter(command, "binary", new byte[] { 1 })).SqlDbType);
        Assert.Equal(
            SqlDbType.Time,
            ((SqlParameter)source.FindMapping(typeof(TimeSpan))!
                .CreateParameter(command, "interval", TimeSpan.FromMinutes(1))).SqlDbType);
    }

    [Theory]
    [InlineData(typeof(DateTime), null, null, 7)]
    [InlineData(typeof(DateTime), null, 3, 3)]
    [InlineData(typeof(DateTime), null, 0, 0)]
    [InlineData(typeof(DateTime), "TIMESTAMP", null, 6)]
    [InlineData(typeof(DateTime), "TIMESTAMP(4)", null, 4)]
    [InlineData(typeof(DateTime), "DATE", null, 0)]
    [InlineData(typeof(DateTimeOffset), null, null, 7)]
    [InlineData(typeof(DateTimeOffset), null, 5, 5)]
    [InlineData(typeof(DateTimeOffset), "DATETIME WITH TIME ZONE", null, 6)]
    [InlineData(typeof(TimeOnly), null, null, 6)]
    [InlineData(typeof(TimeOnly), null, 2, 2)]
    [InlineData(typeof(TimeSpan), null, null, 6)]
    public void DriverReceivesTheMappingsFractionalSecondPrecision(
        Type clrType,
        string? storeType,
        int? precision,
        int expectedScale)
    {
        using var context = new MappingContext(CreateFakeOptions<MappingContext>());
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = storeType is null
            ? source.FindMapping(clrType, null, precision: precision)!
            : source.FindMapping(clrType, storeType)!;
        using var command = new SqlCommand();

        var nullParameter = mapping.CreateParameter(command, "p0", null);
        var value = clrType == typeof(DateTime) ? new DateTime(2026, 10, 14)
            : clrType == typeof(DateTimeOffset) ? new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.FromHours(8))
            : clrType == typeof(TimeOnly) ? new TimeOnly(1, 2, 3)
            : (object)TimeSpan.FromSeconds(1);
        var valueParameter = mapping.CreateParameter(command, "p1", value);

        foreach (var parameter in new[] { nullParameter, valueParameter })
        {
            Assert.Equal(expectedScale, parameter.Scale);
            Assert.Equal(0, parameter.Precision);
        }
    }

    [Fact]
    public void OfficialDriverKeepsTextTemporalReadersAndTypedDriversUseTypedGetters()
    {
        using var officialContext = new MappingContext(CreateOfficialOptions<MappingContext>());
        using var typedContext = new MappingContext(CreateFakeOptions<MappingContext>());
        var getString = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetString), [typeof(int)]);

        foreach (var clrType in new[] { typeof(DateTimeOffset), typeof(TimeSpan) })
        {
            var officialMapping = officialContext.GetService<IRelationalTypeMappingSource>().FindMapping(clrType)!;
            var typedMapping = typedContext.GetService<IRelationalTypeMappingSource>().FindMapping(clrType)!;

            Assert.Equal(getString, officialMapping.GetDataReaderMethod());
            var typedMethod = typedMapping.GetDataReaderMethod();
            Assert.True(typedMethod.IsGenericMethod);
            Assert.Equal(nameof(DbDataReader.GetFieldValue), typedMethod.Name);
            Assert.Equal(clrType, typedMethod.GetGenericArguments().Single());
        }
    }

    [Fact]
    public void DesignTimeServicesUseTheSelectedDriver()
    {
        var services = DamengDriverConfiguration
            .AddDesignTimeServices(new ServiceCollection(), FakeDamengDriver.Instance)
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IProviderConfigurationCodeGenerator>();
        var call = generator.GenerateUseProvider(ConnectionString, providerOptions: null);
        var factory = services.GetRequiredService<IDatabaseModelFactory>();
        using var foreignConnection = new DmConnection();

        Assert.Equal(nameof(UseFakeDameng), call.Method);
        Assert.Equal(typeof(DamengDriverSelectionTests).Name, call.DeclaringType);
        var error = Assert.Throws<ArgumentException>(
            () => factory.Create(foreignConnection, new DatabaseModelFactoryOptions()));
        Assert.Equal("Dameng:ForeignConnection", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    [Fact]
    public void OfficialDatabaseModelFactoryRejectsConnectionsOfAnotherDriver()
    {
        var services = new ServiceCollection();
        new W.EntityFrameworkCore.Dameng.Design.Internal.DamengDesignTimeServices()
            .ConfigureDesignTimeServices(services);
        using var provider = services.BuildServiceProvider();
        using var foreignConnection = new SqlConnection();

        var error = Assert.Throws<ArgumentException>(
            () => provider.GetRequiredService<IDatabaseModelFactory>()
                .Create(foreignConnection, new DatabaseModelFactoryOptions()));

        Assert.Equal("Dameng:ForeignConnection", error.Data[DamengDriverErrors.ErrorIdKey]);
    }

    public static DbContextOptionsBuilder UseFakeDameng(
        DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => DamengDriverConfiguration.UseDriver(
            optionsBuilder,
            FakeDamengDriver.Instance,
            connectionString,
            damengOptionsAction);

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
        Assert.Equal("Dameng:RetryNotSupportedForFakeDriver", error.Data[DamengDriverErrors.ErrorIdKey]);
        Assert.StartsWith("Dameng:RetryNotSupportedForFakeDriver", error.Message, StringComparison.Ordinal);
    }

    private static DamengDriver? GetBoundDriver(DbContext context, string propertyName)
        => ((IDamengDriverBoundMapping)context.Model.FindEntityType(typeof(MappingEntity))!
            .FindProperty(propertyName)!
            .GetRelationalTypeMapping()).Driver;

    private static DbContextOptions CreateOfficialOptions()
        => new DbContextOptionsBuilder().UseDameng(ConnectionString).Options;

    private static DbContextOptions CreateFakeOptions()
        => DamengDriverConfiguration.UseDriver(new DbContextOptionsBuilder(), FakeDamengDriver.Instance, ConnectionString, null)
            .Options;

    private static DbContextOptions<TContext> CreateOfficialOptions<TContext>(
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>().UseDameng(ConnectionString, damengOptionsAction).Options;

    private static DbContextOptions<TContext> CreateFakeOptions<TContext>(
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        DamengDriverConfiguration.UseDriver(builder, FakeDamengDriver.Instance, ConnectionString, damengOptionsAction);
        return builder.Options;
    }

    /// <summary>
    /// A test driver that uses SqlClient types so driver selection can be verified without W.
    /// </summary>
    private sealed class FakeDamengDriver : DamengDriver
    {
        public static FakeDamengDriver Instance { get; } = new();

        public override string Name
            => "Fake";

        public override string ProviderName
            => "W.EntityFrameworkCore.Dameng.Tests.Fake";

        public override MethodInfo UseProviderMethod
            => typeof(DamengDriverSelectionTests).GetMethod(nameof(UseFakeDameng))!;

        public override bool UsesTypedTemporalReaders
            => true;

        public override DbConnection CreateConnection(string connectionString)
            => new SqlConnection();

        public override bool IsDriverConnection(DbConnection connection)
            => connection is SqlConnection;

        public override void SetParameterType(DbParameter parameter, DamengParameterType type)
        {
            if (parameter is not SqlParameter sqlParameter)
            {
                throw CreateForeignParameterException(parameter);
            }

            sqlParameter.SqlDbType = type switch
            {
                DamengParameterType.Blob => SqlDbType.Image,
                DamengParameterType.Clob => SqlDbType.Text,
                DamengParameterType.Binary => SqlDbType.Binary,
                DamengParameterType.VarBinary => SqlDbType.VarBinary,
                DamengParameterType.IntervalDayToSecond => SqlDbType.Time,
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }

        public override void ConfigureTemporalParameter(DbParameter parameter, int fractionalSecondPrecision)
        {
            parameter.Precision = 0;
            parameter.Scale = checked((byte)fractionalSecondPrecision);
        }
    }

    private sealed class MappingContext(DbContextOptions<MappingContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<MappingEntity>();
    }

    private sealed class MappingEntity
    {
        public int Id { get; set; }

        public string? Text { get; set; }
    }
}
