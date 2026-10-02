using Microsoft.EntityFrameworkCore.Metadata;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Dameng-specific metadata extensions for EF Core properties.
/// </summary>
public static class DamengPropertyExtensions
{
    /// <summary>
    /// Gets the effective Dameng value generation strategy for a property.
    /// </summary>
    public static DamengValueGenerationStrategy GetDamengValueGenerationStrategy(
        this IReadOnlyProperty property)
    {
        if (property.FindAnnotation(DamengAnnotationNames.ValueGenerationStrategy)?.Value
            is DamengValueGenerationStrategy configuredStrategy)
        {
            return configuredStrategy;
        }

        if (property.ValueGenerated != ValueGenerated.OnAdd
            || property.GetContainingForeignKeys().Any(
                foreignKey => foreignKey.DeclaringEntityType == property.DeclaringType)
            || property.TryGetDefaultValue(out _)
            || property.GetComputedColumnSql() is not null)
        {
            return DamengValueGenerationStrategy.None;
        }

        var modelStrategy = property.DeclaringType.Model.GetDamengValueGenerationStrategy();
        if (modelStrategy == DamengValueGenerationStrategy.Sequence)
        {
            return IsCompatibleWithDatabaseGeneratedInteger(property)
                ? DamengValueGenerationStrategy.Sequence
                : DamengValueGenerationStrategy.None;
        }

        if (property.GetDefaultValueSql() is not null)
        {
            return DamengValueGenerationStrategy.None;
        }

        return modelStrategy == DamengValueGenerationStrategy.IdentityColumn
            && IsCompatibleWithIdentity(property)
                ? DamengValueGenerationStrategy.IdentityColumn
                : DamengValueGenerationStrategy.None;
    }

    /// <summary>
    /// Gets the effective Dameng value generation strategy for a property in a
    /// specific store object.
    /// </summary>
    public static DamengValueGenerationStrategy GetDamengValueGenerationStrategy(
        this IReadOnlyProperty property,
        in StoreObjectIdentifier storeObject)
    {
        var strategy = property.GetDamengValueGenerationStrategy();
        if (strategy == DamengValueGenerationStrategy.None
            || storeObject.StoreObjectType != StoreObjectType.Table
            || property.GetColumnName(storeObject) is null)
        {
            return DamengValueGenerationStrategy.None;
        }

        // An inherited generated key in TPC belongs to each concrete table. A
        // sequence strategy shares one sequence across those tables; identity is
        // valid only when the hierarchy maps to one table and is rejected by the
        // model validator otherwise. In TPT, store generation still belongs only
        // to the declaring type's main table because the derived key is an FK copy.
        if (property.DeclaringType is IReadOnlyEntityType declaringEntityType
            && declaringEntityType.GetRootType().GetMappingStrategy()
                == RelationalAnnotationNames.TpcMappingStrategy
            && property.GetMappedStoreObjects(StoreObjectType.Table)
                .Contains(storeObject))
        {
            return strategy;
        }

        var mainStoreObject = StoreObjectIdentifier.Create(
            property.DeclaringType,
            StoreObjectType.Table);

        return mainStoreObject == storeObject
            ? strategy
            : DamengValueGenerationStrategy.None;
    }

    /// <summary>
    /// Sets the Dameng value generation strategy for a property.
    /// </summary>
    public static void SetDamengValueGenerationStrategy(
        this IMutableProperty property,
        DamengValueGenerationStrategy? strategy)
        => property.SetOrRemoveAnnotation(DamengAnnotationNames.ValueGenerationStrategy, strategy);

    /// <summary>
    /// Sets the Dameng value generation strategy for a property.
    /// </summary>
    public static DamengValueGenerationStrategy? SetDamengValueGenerationStrategy(
        this IConventionProperty property,
        DamengValueGenerationStrategy? strategy,
        bool fromDataAnnotation = false)
        => (DamengValueGenerationStrategy?)property.SetOrRemoveAnnotation(
            DamengAnnotationNames.ValueGenerationStrategy,
            strategy,
            fromDataAnnotation)?.Value;

    /// <summary>
    /// Gets the identity seed configured for a property.
    /// </summary>
    public static long GetDamengIdentitySeed(this IReadOnlyProperty property)
        => (long?)property[DamengAnnotationNames.IdentitySeed]
            ?? property.DeclaringType.Model.GetDamengIdentitySeed();

    /// <summary>
    /// Sets the identity seed for a property.
    /// </summary>
    public static void SetDamengIdentitySeed(this IMutableProperty property, long? seed)
        => property.SetOrRemoveAnnotation(DamengAnnotationNames.IdentitySeed, seed);

    /// <summary>
    /// Gets the identity increment configured for a property.
    /// </summary>
    public static int GetDamengIdentityIncrement(this IReadOnlyProperty property)
        => (int?)property[DamengAnnotationNames.IdentityIncrement]
            ?? property.DeclaringType.Model.GetDamengIdentityIncrement();

    /// <summary>
    /// Sets the identity increment for a property.
    /// </summary>
    public static void SetDamengIdentityIncrement(this IMutableProperty property, int? increment)
        => property.SetOrRemoveAnnotation(DamengAnnotationNames.IdentityIncrement, increment);

    /// <summary>
    /// Gets the sequence name configured for a property.
    /// </summary>
    public static string? GetDamengSequenceName(this IReadOnlyProperty property)
        => (string?)property[DamengAnnotationNames.SequenceName];

    /// <summary>
    /// Sets the sequence name for a property.
    /// </summary>
    public static void SetDamengSequenceName(this IMutableProperty property, string? name)
        => property.SetOrRemoveAnnotation(DamengAnnotationNames.SequenceName, CheckNullButNotEmpty(name));

    /// <summary>
    /// Gets the sequence schema configured for a property.
    /// </summary>
    public static string? GetDamengSequenceSchema(this IReadOnlyProperty property)
        => (string?)property[DamengAnnotationNames.SequenceSchema]
            ?? property.DeclaringType.Model.GetDefaultSchema();

    /// <summary>
    /// Sets the sequence schema for a property.
    /// </summary>
    public static void SetDamengSequenceSchema(this IMutableProperty property, string? schema)
        => property.SetOrRemoveAnnotation(DamengAnnotationNames.SequenceSchema, CheckNullButNotEmpty(schema));

    internal static bool IsCompatibleWithDatabaseGeneratedInteger(IReadOnlyProperty property)
        => IsCompatibleWithDatabaseGeneratedInteger(GetProviderClrType(property));

    internal static bool IsCompatibleWithDatabaseGeneratedInteger(Type type)
    {
        return type == typeof(sbyte)
            || type == typeof(byte)
            || type == typeof(short)
            || type == typeof(int)
            || type == typeof(long);
    }

    internal static bool IsCompatibleWithIdentity(IReadOnlyProperty property)
    {
        var type = GetProviderClrType(property);

        return type == typeof(int)
            || type == typeof(long);
    }

    internal static void EnsureCompatible(
        IReadOnlyProperty property,
        DamengValueGenerationStrategy? strategy)
    {
        var compatible = strategy switch
        {
            DamengValueGenerationStrategy.IdentityColumn => IsCompatibleWithIdentity(property),
            DamengValueGenerationStrategy.Sequence => IsCompatibleWithDatabaseGeneratedInteger(property),
            _ => true
        };

        if (!compatible)
        {
            throw new ArgumentException(
                $"The property '{property.DeclaringType.DisplayName()}.{property.Name}' has CLR type "
                + $"'{property.ClrType.Name}', which cannot use the Dameng '{strategy}' "
                + "value generation strategy.",
                nameof(strategy));
        }
    }

    private static Type GetProviderClrType(IReadOnlyProperty property)
    {
        var configuredType = property.GetProviderClrType()
            ?? property.GetValueConverter()?.ProviderClrType
            ?? property.ClrType;
        var type = Nullable.GetUnderlyingType(configuredType) ?? configuredType;

        return type.IsEnum ? Enum.GetUnderlyingType(type) : type;
    }

    private static string? CheckNullButNotEmpty(string? value)
    {
        if (value is { Length: 0 })
        {
            throw new ArgumentException("The value cannot be an empty string.", nameof(value));
        }

        return value;
    }
}
