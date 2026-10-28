using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    : IMethodCallTranslator
{
    private static readonly MethodInfo AddYears
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), [typeof(int)])!;

    private static readonly MethodInfo AddMonths
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), [typeof(int)])!;

    private static readonly MethodInfo AddDays
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), [typeof(double)])!;

    private static readonly MethodInfo AddHours
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), [typeof(double)])!;

    private static readonly MethodInfo AddMinutes
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), [typeof(double)])!;

    private static readonly MethodInfo AddSeconds
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), [typeof(double)])!;

    private static readonly MethodInfo AddMilliseconds
        = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMilliseconds), [typeof(double)])!;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null)
        {
            return null;
        }

        var datePart = GetDatePart(method);
        if (datePart is null
            || !CanRepresentAsDamengDateAddUnit(method, arguments[0]))
        {
            return null;
        }

        DamengTemporalTranslationGuard.EnsureNative(instance);

        return sqlExpressionFactory.Function(
            "DATEADD",
            [
                sqlExpressionFactory.Fragment(datePart),
                sqlExpressionFactory.Convert(arguments[0], typeof(int)),
                instance
            ],
            nullable: true,
            argumentsPropagateNullability: [false, true, true],
            instance.Type,
            instance.TypeMapping);
    }

    private static string? GetDatePart(MethodInfo method)
        => method == AddYears
            ? "year"
            : method == AddMonths
                ? "month"
                : method == AddDays
                    ? "day"
                    : method == AddHours
                        ? "hour"
                        : method == AddMinutes
                            ? "minute"
                            : method == AddSeconds
                                ? "second"
                                : method == AddMilliseconds
                                    ? "millisecond"
                                    : null;

    private static bool CanRepresentAsDamengDateAddUnit(
        MethodInfo method,
        SqlExpression argument)
    {
        if (method == AddYears || method == AddMonths)
        {
            return true;
        }

        return argument is SqlConstantExpression { Value: double value }
            && double.IsFinite(value)
            && value == Math.Truncate(value)
            && value is >= int.MinValue and <= int.MaxValue;
    }
}
