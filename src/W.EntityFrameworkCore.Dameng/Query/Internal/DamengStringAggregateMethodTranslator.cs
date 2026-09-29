using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Translates the IEnumerable string.Join/Concat group aggregate overloads to LISTAGG.
/// </summary>
internal sealed class DamengStringAggregateMethodTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource)
    : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo JoinMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Join), [typeof(string), typeof(IEnumerable<string>)])!;

    private static readonly MethodInfo ConcatMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Concat), [typeof(IEnumerable<string>)])!;

    private static readonly HashSet<string> BoundedTextStoreTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "VARCHAR", "VARCHAR2", "CHAR VARYING", "CHARACTER VARYING",
        "NVARCHAR", "NVARCHAR2", "NATIONAL CHAR VARYING", "NATIONAL CHARACTER VARYING"
    };

    public SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != JoinMethod && method != ConcatMethod)
        {
            return null;
        }

        if (source.IsDistinct)
        {
            throw new NotSupportedException("Dameng string.Join/Concat aggregation does not support Distinct().");
        }

        if (source.Selector is not SqlExpression { Type: not null } selector
            || selector.Type != typeof(string)
            || !IsBoundedText(selector.TypeMapping))
        {
            throw new NotSupportedException(
                "Dameng string.Join/Concat aggregation requires a bounded varying text selector; LOB, fixed CHAR, and unmapped text are unsupported.");
        }

        if (source.Orderings.Any(ordering => IsLob(ordering.Expression.TypeMapping)))
        {
            throw new NotSupportedException("Dameng string.Join/Concat aggregation cannot order by a LOB expression.");
        }

        // The aggregate output may be much longer than a single item. Keep the result
        // varying and bounded, even when the source column is short text.
        var resultMapping = typeMappingSource.FindMapping(
                typeof(string), storeTypeName: null, unicode: true,
                size: DamengTypeMappingSource.MaxInlineLength)
            ?? throw new InvalidOperationException("Dameng bounded string result mapping is unavailable.");
        var empty = sqlExpressionFactory.ApplyTypeMapping(
            sqlExpressionFactory.Constant(string.Empty), resultMapping);

        SqlExpression input;
        SqlExpression aggregateSeparator;
        SqlExpression? normalizedSeparator = null;
        if (method == JoinMethod)
        {
            if (arguments.Count != 1)
            {
                throw new InvalidOperationException("The string.Join aggregate requires one separator argument.");
            }

            var separator = sqlExpressionFactory.ApplyTypeMapping(arguments[0], resultMapping);
            if (IsLob(separator.TypeMapping))
            {
                throw new NotSupportedException("Dameng string.Join aggregation requires a bounded text separator.");
            }

            // CLR string.Join treats a null separator as empty.
            normalizedSeparator = sqlExpressionFactory.Coalesce(separator, empty);
            // LISTAGG skips null and empty items. A nonempty separator prefix keeps
            // both in the aggregate without using a sentinel that can collide with data.
            input = sqlExpressionFactory.Add(
                normalizedSeparator,
                sqlExpressionFactory.Coalesce(selector, empty));
            aggregateSeparator = empty;
        }
        else
        {
            input = selector;
            aggregateSeparator = empty;
        }

        if (source.Predicate is not null)
        {
            // Put the filter around the complete prefixed value. Coalescing the
            // selector after filtering would incorrectly restore excluded rows.
            input = sqlExpressionFactory.Case(
                [new CaseWhenClause(source.Predicate, input)],
                elseResult: null);
        }

        var aggregate = new DamengListAggExpression(
            input, aggregateSeparator, source.Orderings, resultMapping);
        if (method == ConcatMethod)
        {
            return sqlExpressionFactory.Coalesce(aggregate, empty);
        }

        var prefixLength = sqlExpressionFactory.Coalesce(
            sqlExpressionFactory.Function(
                "LENGTH", [normalizedSeparator!], nullable: true,
                argumentsPropagateNullability: [true], typeof(int), typeMapping: null),
            sqlExpressionFactory.Constant(0));
        var start = sqlExpressionFactory.Add(prefixLength, sqlExpressionFactory.Constant(1));
        var withoutFirstPrefix = sqlExpressionFactory.Function(
            "SUBSTR", [aggregate, start], nullable: true,
            argumentsPropagateNullability: [true, true], typeof(string), resultMapping);
        return sqlExpressionFactory.Coalesce(withoutFirstPrefix, empty);
    }

    private static bool IsBoundedText(RelationalTypeMapping? mapping)
        => mapping is { ClrType: not null, Size: > 0 and <= DamengTypeMappingSource.MaxInlineLength }
            && mapping.ClrType == typeof(string)
            && BoundedTextStoreTypes.Contains(mapping.StoreTypeNameBase);

    private static bool IsLob(RelationalTypeMapping? mapping)
        => mapping is not null
            && (mapping.StoreTypeNameBase.Equals("CLOB", StringComparison.OrdinalIgnoreCase)
                || mapping.StoreTypeNameBase.Equals("NCLOB", StringComparison.OrdinalIgnoreCase)
                || mapping.StoreTypeNameBase.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                || mapping.StoreTypeNameBase.Equals("NTEXT", StringComparison.OrdinalIgnoreCase)
                || mapping.StoreTypeNameBase.Equals("LONG", StringComparison.OrdinalIgnoreCase)
                || mapping.StoreTypeNameBase.Equals("LONGVARCHAR", StringComparison.OrdinalIgnoreCase));
}
