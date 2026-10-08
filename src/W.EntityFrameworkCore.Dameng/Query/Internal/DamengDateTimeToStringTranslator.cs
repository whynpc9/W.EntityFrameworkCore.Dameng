using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Translates temporal text conversion using Dameng's server-side formatting contract.
/// </summary>
internal sealed class DamengDateTimeToStringTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource) : IMethodCallTranslator
{
    private static readonly MethodInfo DateTimeToString
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.ToString), Type.EmptyTypes)!;
    private static readonly MethodInfo NullableDateTimeToString
        = typeof(DateTime?).GetRuntimeMethod(nameof(DateTime.ToString), Type.EmptyTypes)!;
    private static readonly MethodInfo ObjectToString
        = typeof(object).GetRuntimeMethod(nameof(object.ToString), Type.EmptyTypes)!;
    private static readonly MethodInfo FormattedDateTimeToString
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.ToString), [typeof(string)])!;

    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method,
        IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null)
        {
            return null;
        }

        var nullableConversion = method == NullableDateTimeToString
            || (method == ObjectToString && (Nullable.GetUnderlyingType(instance.Type) ?? instance.Type) == typeof(DateTime));
        if (!nullableConversion && method != DateTimeToString && method != FormattedDateTimeToString)
        {
            return null;
        }

        // A DateTime converted to ticks/text is not a native temporal SQL operand.
        if (instance.TypeMapping?.Converter is { } converter && converter.ProviderClrType != typeof(DateTime))
        {
            return null;
        }

        // Keep the result bounded: GROUP BY and text predicates cannot operate on a CLOB.
        var textMapping = typeMappingSource.FindMapping(typeof(string), storeTypeName: "VARCHAR(100)")
            ?? throw new InvalidOperationException("Dameng temporal text mapping is unavailable.");
        SqlExpression result;
        if (method == FormattedDateTimeToString)
        {
            if (arguments[0] is not SqlConstantExpression { Value: string format }
                || GetServerFormat(format) is not { } serverFormat)
            {
                return null;
            }

            result = sqlExpressionFactory.Function("TO_CHAR",
                [instance, sqlExpressionFactory.Constant(serverFormat, textMapping)],
                nullable: true, argumentsPropagateNullability: [true, false], typeof(string), textMapping);
        }
        else
        {
            // The server chooses its default DATE/TIMESTAMP textual representation.
            // No business granularity or client culture is inferred here.
            result = sqlExpressionFactory.Convert(instance, typeof(string), textMapping);
        }

        return nullableConversion
            ? sqlExpressionFactory.Coalesce(result, sqlExpressionFactory.Constant(string.Empty, textMapping))
            : result;
    }

    private static string? GetServerFormat(string format) => format switch
    {
        "yyyy" => "YYYY",
        "yyyy-MM" => "YYYY-MM",
        "yyyy-MM-dd" => "YYYY-MM-DD",
        "yyyy-MM-dd HH:mm:ss" => "YYYY-MM-DD HH24:MI:SS",
        "yyyy-MM-dd HH:mm:ss.fffffff" => "YYYY-MM-DD HH24:MI:SS.FF7",
        "s" or "yyyy-MM-dd'T'HH:mm:ss" => "YYYY-MM-DD\"T\"HH24:MI:SS",
        _ => null
    };
}
