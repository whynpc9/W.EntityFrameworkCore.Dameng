using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using W.EntityFrameworkCore.Dameng.Design.Internal;
using W.EntityFrameworkCore.Dameng.Diagnostics.Internal;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;
using W.EntityFrameworkCore.Dameng.Metadata.Conventions;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;
using W.EntityFrameworkCore.Dameng.Migrations.Internal;
using W.EntityFrameworkCore.Dameng.Query.Internal;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using W.EntityFrameworkCore.Dameng.Update.Internal;

namespace W.EntityFrameworkCore.Dameng.Infrastructure;

/// <summary>
/// Configures the shared Dameng EF Core provider for a specific driver.
/// </summary>
/// <remarks>
/// This is infrastructure for Dameng driver packages. Applications call the driver's own
/// <c>Use...</c> and <c>AddEntityFramework...</c> methods instead.
/// </remarks>
public static class DamengDriverConfiguration
{
    /// <summary>
    /// Configures a context to connect to Dameng Database with the supplied driver and connection string.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="driver">The driver to select.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder.</returns>
    public static DbContextOptionsBuilder UseDriver(
        DbContextOptionsBuilder optionsBuilder,
        DamengDriver driver,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(driver);

        var extension = (DamengOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithDriver(driver)
            .WithConnectionString(connectionString);

        return Apply(optionsBuilder, extension, damengOptionsAction);
    }

    /// <summary>
    /// Configures a context to connect to Dameng Database with the supplied driver and an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="driver">The driver to select.</param>
    /// <param name="connection">The database connection; it must be a connection of <paramref name="driver" />.</param>
    /// <param name="contextOwnsConnection">Whether the context disposes the connection.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder.</returns>
    public static DbContextOptionsBuilder UseDriver(
        DbContextOptionsBuilder optionsBuilder,
        DamengDriver driver,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(connection);

        if (!driver.IsDriverConnection(connection))
        {
            throw DamengDriverErrors.ForeignConnection(driver, connection);
        }

        var extension = (DamengOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithDriver(driver)
            .WithConnection(connection, contextOwnsConnection);

        return Apply(optionsBuilder, extension, damengOptionsAction);
    }

    /// <summary>
    /// Adds the Dameng EF Core provider services for the supplied driver.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="driver">The driver whose services are registered.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Registering services for two different drivers in one collection is reported as an error
    /// when a context first uses the resulting internal service provider.
    /// </remarks>
    public static IServiceCollection AddEntityFrameworkDameng(
        IServiceCollection services,
        DamengDriver driver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(driver);

        services.TryAddSingleton(driver);
        services.AddSingleton(new DamengDriverRegistration(driver));

        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<LoggingDefinitions, DamengLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DamengDatabaseProvider>()
            .TryAdd<ISingletonOptions, DamengDriverSingletonOptions>()
            .TryAdd<IModelValidator, DamengModelValidator>()
            .TryAdd<IRelationalAnnotationProvider, DamengRelationalAnnotationProvider>()
            .TryAdd<IRelationalTypeMappingSource, DamengTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, DamengSqlGenerationHelper>()
            .TryAdd<IProviderConventionSetBuilder, DamengConventionSetBuilder>()
            .TryAdd<IRelationalConnection, DamengRelationalConnection>()
            .TryAdd<IMigrationsSqlGenerator, DamengMigrationsSqlGenerator>()
            .TryAdd<IRelationalDatabaseCreator, DamengDatabaseCreator>()
            .TryAdd<IHistoryRepository, DamengHistoryRepository>()
            .TryAdd<IQuerySqlGeneratorFactory, DamengQuerySqlGeneratorFactory>()
            .TryAdd<
                IRelationalParameterBasedSqlProcessorFactory,
                DamengParameterBasedSqlProcessorFactory>()
            .TryAdd<IMemberTranslatorProvider, DamengMemberTranslatorProvider>()
            .TryAdd<IMethodCallTranslatorProvider, DamengMethodCallTranslatorProvider>()
            .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory, DamengSqlTranslatingExpressionVisitorFactory>()
            .TryAdd<IAggregateMethodCallTranslatorProvider, DamengAggregateMethodCallTranslatorProvider>()
            .TryAdd<IUpdateSqlGenerator, DamengUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, DamengModificationCommandBatchFactory>()
            .TryAddCoreServices();

        return services;
    }

    /// <summary>
    /// Adds the services used by Entity Framework Core tools for the supplied driver.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="driver">The driver whose design-time services are registered.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddDesignTimeServices(
        IServiceCollection services,
        DamengDriver driver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(driver);

        AddEntityFrameworkDameng(services, driver);

        new EntityFrameworkRelationalDesignServicesBuilder(services)
            .TryAdd<IAnnotationCodeGenerator, DamengAnnotationCodeGenerator>()
            .TryAdd<IProviderConfigurationCodeGenerator, DamengCodeGenerator>()
            .TryAdd<IDatabaseModelFactory, DamengDatabaseModelFactory>()
            .TryAddCoreServices();

        return services;
    }

    private static DamengOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<DamengOptionsExtension>()
            ?? new DamengOptionsExtension();

    private static DbContextOptionsBuilder Apply(
        DbContextOptionsBuilder optionsBuilder,
        DamengOptionsExtension extension,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction)
    {
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        ConfigureWarnings(optionsBuilder);
        damengOptionsAction?.Invoke(new DamengDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
            ?? new CoreOptionsExtension();

        coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}
