using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Diagnostics.Internal;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;
using W.EntityFrameworkCore.Dameng.Metadata.Conventions;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;
using W.EntityFrameworkCore.Dameng.Migrations.Internal;
using W.EntityFrameworkCore.Dameng.Query.Internal;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using W.EntityFrameworkCore.Dameng.Update.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection registration methods for the Dameng EF Core provider.
/// </summary>
public static class DamengServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services used by the Dameng EF Core provider.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection so additional calls can be chained.</returns>
    public static IServiceCollection AddEntityFrameworkDameng(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<LoggingDefinitions, DamengLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<DamengOptionsExtension>>()
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
            .TryAdd<IAggregateMethodCallTranslatorProvider, DamengAggregateMethodCallTranslatorProvider>()
            .TryAdd<IUpdateSqlGenerator, DamengUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, DamengModificationCommandBatchFactory>()
            .TryAddCoreServices();

        return services;
    }
}
