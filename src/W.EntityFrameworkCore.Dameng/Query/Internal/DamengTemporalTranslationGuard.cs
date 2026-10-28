using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>Temporal functions require temporal SQL storage, independent of the model CLR type.</summary>
internal static class DamengTemporalTranslationGuard
{
    public static void EnsureNative(SqlExpression value)
    {
        var mapping = value.TypeMapping;
        // Unmapped static/function expressions are mapped later by EF. Bound columns already
        // have an effective mapping, including a composed converter's provider CLR type.
        if (mapping is null) return;
        var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
        providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
        // StoreTypeNameBase drops the suffix after a precision facet (for example
        // DATETIME(7) WITH TIME ZONE), so retain that suffix when classifying.
        var storeType = mapping.StoreType.Trim().ToUpperInvariant();
        var open = storeType.IndexOf('(');
        var close = storeType.IndexOf(')');
        if (open >= 0 && close > open) storeType = storeType.Remove(open, close - open + 1);
        var native = providerType == typeof(DateTime)
            ? storeType is "DATE" or "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "TIMESTAMP" or "TIMESTAMP WITH LOCAL TIME ZONE"
            : providerType == typeof(DateTimeOffset)
                && storeType is "DATETIME WITH TIME ZONE" or "TIMESTAMP WITH TIME ZONE";
        if (!native)
        {
            throw new InvalidOperationException(
                $"Dameng date operations require a native temporal store mapping; '{mapping.StoreType}' is unsupported.");
        }
    }
}
