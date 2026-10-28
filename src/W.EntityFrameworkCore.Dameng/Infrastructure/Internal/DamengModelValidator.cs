using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

namespace W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

internal sealed class DamengModelValidator(
    ModelValidatorDependencies dependencies,
    RelationalModelValidatorDependencies relationalDependencies)
    : RelationalModelValidator(dependencies, relationalDependencies)
{
    public override void Validate(
        IModel model,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateLobKeysAndIndexes(model);
        ValidateStoreGeneratedKeys(model);
        ValidateRowVersions(model);
        ValidateDecimalFacets(model);
        ValidateIdentityIncrements(model);
        ValidateTpcIdentityColumns(model);
        ValidateIdentityColumns(model);
    }

    protected override void ValidateSharedColumnsCompatibility(
        IReadOnlyList<IEntityType> mappedTypes,
        in StoreObjectIdentifier storeObject,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.ValidateSharedColumnsCompatibility(mappedTypes, storeObject, logger);

        var generators = new Dictionary<string, IProperty>(StringComparer.Ordinal);
        foreach (var entityType in mappedTypes)
        {
            foreach (var property in entityType.GetFlattenedProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (columnName is null
                    || property.GetDamengValueGenerationStrategy(storeObject) == DamengValueGenerationStrategy.None)
                {
                    continue;
                }

                if (generators.TryGetValue(columnName, out var duplicateProperty))
                {
                    ValidateCompatibleGeneration(property, duplicateProperty, columnName, storeObject);
                }
                else
                {
                    generators.Add(columnName, property);
                }
            }
        }
    }

    private static void ValidateCompatibleGeneration(
        IProperty property,
        IProperty duplicateProperty,
        string columnName,
        in StoreObjectIdentifier storeObject)
    {
        var strategy = property.GetDamengValueGenerationStrategy(storeObject);
        var duplicateStrategy = duplicateProperty.GetDamengValueGenerationStrategy(storeObject);
        var facet = strategy != duplicateStrategy
            ? "ValueGenerationStrategy"
            : strategy == DamengValueGenerationStrategy.IdentityColumn
                ? property.GetDamengIdentitySeed() != duplicateProperty.GetDamengIdentitySeed()
                    ? "IdentitySeed"
                    : property.GetDamengIdentityIncrement() != duplicateProperty.GetDamengIdentityIncrement()
                        ? "IdentityIncrement" : null
                : !StringComparer.Ordinal.Equals(property.GetDamengSequenceName(), duplicateProperty.GetDamengSequenceName())
                    ? "SequenceName"
                    : !StringComparer.Ordinal.Equals(property.GetDamengSequenceSchema(), duplicateProperty.GetDamengSequenceSchema())
                        ? "SequenceSchema" : null;

        if (facet is not null)
        {
            throw new InvalidOperationException(
                $"The properties '{property.DeclaringType.DisplayName()}.{property.Name}' and "
                + $"'{duplicateProperty.DeclaringType.DisplayName()}.{duplicateProperty.Name}' "
                + $"share column '{columnName}' in table '{storeObject.DisplayName()}' "
                + $"but have conflicting Dameng {facet} values.");
        }
    }

    private static void ValidateLobKeysAndIndexes(IModel model)
    {
        foreach (var property in model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetDeclaredProperties()))
        {
            var isKey = property.IsKey();
            var isIndex = property.GetContainingIndexes().Any();
            if (!isKey && !isIndex)
            {
                continue;
            }

            var mapping = property.GetRelationalTypeMapping();
            var maxLength = property.GetMaxLength();
            var exceedsInlineLimit =
                property.ClrType is { } clrType
                && (clrType == typeof(string) || clrType == typeof(byte[]))
                && maxLength > DamengTypeMappingSource.MaxInlineLength;
            if (!exceedsInlineLimit
                && !IsLobStoreType(mapping.StoreTypeNameBase))
            {
                continue;
            }

            var role = isKey && isIndex
                ? "key/index"
                : isKey
                    ? "key"
                    : "index";

            throw new InvalidOperationException(
                $"The {role} property "
                + $"'{property.DeclaringType.DisplayName()}.{property.Name}' maps to the Dameng "
                + $"store type '{mapping.StoreType}'"
                + (maxLength is null ? null : $" with length {maxLength}")
                + ", which requires a LOB or exceeds Dameng's inline type limit and cannot be "
                + "used in a key or ordinary index. "
                + "Configure a bounded inline store type for this property.");
        }
    }

    private static void ValidateStoreGeneratedKeys(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            if (table is null)
            {
                continue;
            }

            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (!property.IsKey()
                    || property.ValueGenerated
                        is not (ValueGenerated.OnAdd or ValueGenerated.OnAddOrUpdate)
                    || property.GetDamengValueGenerationStrategy(table.Value)
                        is DamengValueGenerationStrategy.IdentityColumn
                            or DamengValueGenerationStrategy.Sequence)
                {
                    continue;
                }

                var storeGeneration = GetStoreGeneration(property, table.Value);
                if (storeGeneration is null)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"The key property '{property.DeclaringType.DisplayName()}.{property.Name}' uses "
                    + $"{storeGeneration}, but the Dameng provider cannot read back store-generated "
                    + "key values for this strategy. Configure the key with UseDamengIdentityColumn(), "
                    + "UseDamengSequence(), or generate its value on the client.");
            }
        }
    }

    private static void ValidateRowVersions(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            ValidateType(entityType, entityType, entityType.DisplayName());
        }

        static void ValidateType(ITypeBase type, IEntityType owner, string path)
        {
            foreach (var property in type.GetDeclaredProperties())
            {
                if (!property.IsConcurrencyToken
                    || property.ValueGenerated != ValueGenerated.OnAddOrUpdate)
                {
                    continue;
                }

                var tables = property.GetMappedStoreObjects(StoreObjectType.Table).ToArray();
                var hasGenerationMechanism = tables.Length == 0
                    ? property.GetComputedColumnSql() is not null
                    : tables.All(table =>
                        property.GetComputedColumnSql(table) is not null
                        || owner.GetTriggers().Any(trigger =>
                            StringComparer.Ordinal.Equals(trigger.GetTableName(), table.Name)
                            && StringComparer.Ordinal.Equals(trigger.GetTableSchema(), table.Schema)));
                if (hasGenerationMechanism)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"The property '{path}.{property.Name}' is configured "
                    + "as a row version, but Dameng does not automatically generate SQL Server-style "
                    + "rowversion values. Use an application-managed concurrency token, a computed column, "
                    + "or a database trigger.");
            }

            foreach (var complexProperty in type.GetDeclaredComplexProperties())
            {
                ValidateType(complexProperty.ComplexType, owner, path + "." + complexProperty.Name);
            }
        }
    }

    private static void ValidateIdentityColumns(IModel model)
    {
        var identityColumnsByTable =
            new Dictionary<StoreObjectIdentifier, Dictionary<string, IProperty>>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            if (table is null)
            {
                continue;
            }

            AddIdentityColumns(
                entityType.GetDeclaredProperties(),
                table.Value,
                identityColumnsByTable);

            foreach (var fragment in entityType.GetMappingFragments(StoreObjectType.Table))
            {
                AddIdentityColumns(
                    entityType.GetDeclaredProperties(),
                    fragment.StoreObject,
                    identityColumnsByTable);
            }
        }

        foreach (var (table, columns) in identityColumnsByTable)
        {
            if (columns.Count <= 1)
            {
                continue;
            }

            var tableName = table.Schema is null
                ? table.Name
                : table.Schema + "." + table.Name;
            var properties = string.Join(
                ", ",
                columns
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                        $"'{pair.Value.DeclaringType.DisplayName()}.{pair.Value.Name}' "
                        + $"(column '{pair.Key}')"));

            throw new InvalidOperationException(
                $"The table '{tableName}' contains multiple Dameng identity columns: {properties}. "
                + "Dameng supports only one identity column per table.");
        }
    }

    private static void ValidateDecimalFacets(IModel model)
    {
        foreach (var property in model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetDeclaredProperties()))
        {
            var mapping = property.GetRelationalTypeMapping();
            var providerType = property.GetProviderClrType()
                ?? property.GetValueConverter()?.ProviderClrType
                ?? mapping.ClrType;
            if ((Nullable.GetUnderlyingType(providerType) ?? providerType) != typeof(decimal))
            {
                continue;
            }

            var precision = property.GetPrecision() ?? mapping.Precision;
            var scale = property.GetScale() ?? mapping.Scale;
            if (precision is <= 0 or > 38)
            {
                throw new InvalidOperationException(
                    $"The decimal property '{property.DeclaringType.DisplayName()}.{property.Name}' "
                    + $"has precision {precision}, but Dameng supports decimal precision from 1 "
                    + "through 38.");
            }

            if (precision is not null
                && scale is not null
                && scale > precision)
            {
                throw new InvalidOperationException(
                    $"The decimal property '{property.DeclaringType.DisplayName()}.{property.Name}' "
                    + $"has scale {scale}, which exceeds its precision {precision}.");
            }
        }
    }

    private static void ValidateIdentityIncrements(IModel model)
    {
        if (model.GetDamengValueGenerationStrategy()
                == DamengValueGenerationStrategy.IdentityColumn
            && model.GetDamengIdentityIncrement() == 0)
        {
            throw new InvalidOperationException(
                "The Dameng model identity increment cannot be zero.");
        }

        foreach (var property in model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetDeclaredProperties()))
        {
            if (property.GetDamengValueGenerationStrategy()
                    == DamengValueGenerationStrategy.IdentityColumn
                && property.GetDamengIdentityIncrement() == 0)
            {
                throw new InvalidOperationException(
                    $"The Dameng identity increment for "
                    + $"'{property.DeclaringType.DisplayName()}.{property.Name}' cannot be zero.");
            }
        }
    }

    private static void ValidateTpcIdentityColumns(IModel model)
    {
        foreach (var rootType in model.GetEntityTypes()
                     .Where(entityType =>
                         entityType.BaseType is null
                         && entityType.GetMappingStrategy()
                            == RelationalAnnotationNames.TpcMappingStrategy))
        {
            foreach (var property in rootType.GetDeclaredProperties())
            {
                if (property.GetDamengValueGenerationStrategy()
                    != DamengValueGenerationStrategy.IdentityColumn)
                {
                    continue;
                }

                var tables = property.GetMappedStoreObjects(StoreObjectType.Table)
                    .Distinct()
                    .OrderBy(storeObject => storeObject.Schema, StringComparer.Ordinal)
                    .ThenBy(storeObject => storeObject.Name, StringComparer.Ordinal)
                    .ToArray();
                if (tables.Length <= 1)
                {
                    continue;
                }

                var tableNames = string.Join(
                    ", ",
                    tables.Select(
                        table => table.Schema is null
                            ? $"'{table.Name}'"
                            : $"'{table.Schema}.{table.Name}'"));

                throw new InvalidOperationException(
                    $"The TPC key property "
                    + $"'{property.DeclaringType.DisplayName()}.{property.Name}' uses a Dameng "
                    + $"identity column across multiple concrete tables ({tableNames}). "
                    + "Independent identity columns can generate duplicate keys across a TPC "
                    + "hierarchy. Configure the key with UseDamengSequence() so every concrete "
                    + "table draws values from one shared sequence.");
            }
        }
    }

    private static void AddIdentityColumns(
        IEnumerable<IProperty> properties,
        StoreObjectIdentifier table,
        Dictionary<StoreObjectIdentifier, Dictionary<string, IProperty>> identityColumnsByTable)
    {
        foreach (var property in properties)
        {
            if (property.GetDamengValueGenerationStrategy(table)
                    != DamengValueGenerationStrategy.IdentityColumn
                || property.GetColumnName(table) is not { } columnName)
            {
                continue;
            }

            if (!identityColumnsByTable.TryGetValue(table, out var columns))
            {
                columns = new Dictionary<string, IProperty>(StringComparer.Ordinal);
                identityColumnsByTable.Add(table, columns);
            }

            columns.TryAdd(columnName, property);
        }
    }

    private static string? GetStoreGeneration(
        IReadOnlyProperty property,
        in StoreObjectIdentifier table)
        => property.TryGetDefaultValue(table, out _)
            ? "a default value"
            : property.GetDefaultValueSql(table) is not null
                ? "default-value SQL"
                : property.GetComputedColumnSql(table) is not null
                    ? "computed-column SQL"
                    : null;

    private static bool IsLobStoreType(string storeTypeNameBase)
        => storeTypeNameBase.Equals("CLOB", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("NCLOB", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("NTEXT", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("LONG", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("LONGVARCHAR", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("BLOB", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("IMAGE", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("LONGVARBINARY", StringComparison.OrdinalIgnoreCase)
            || storeTypeNameBase.Equals("BFILE", StringComparison.OrdinalIgnoreCase);
}
