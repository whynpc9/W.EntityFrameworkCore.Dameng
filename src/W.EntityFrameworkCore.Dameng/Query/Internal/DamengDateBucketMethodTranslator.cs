using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengDateBucketMethodTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource)
    : IMethodCallTranslator
{
    private static readonly Type Functions = typeof(DamengDateTimeDbFunctionsExtensions);

    private static readonly MethodInfo Hour = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengTruncateHour), typeof(DateTime));
    private static readonly MethodInfo NullableHour = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengTruncateHour), typeof(DateTime?));
    private static readonly MethodInfo Minute = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengTruncateMinute), typeof(DateTime));
    private static readonly MethodInfo NullableMinute = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengTruncateMinute), typeof(DateTime?));
    private static readonly MethodInfo IsoWeek = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengStartOfIsoWeek), typeof(DateTime));
    private static readonly MethodInfo NullableIsoWeek = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengStartOfIsoWeek), typeof(DateTime?));
    private static readonly MethodInfo Quarter = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengQuarter), typeof(DateTime));
    private static readonly MethodInfo NullableQuarter = Find(nameof(DamengDateTimeDbFunctionsExtensions.DamengQuarter), typeof(DateTime?));

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not null || arguments.Count != 2)
        {
            return null;
        }

        var value = arguments[1];
        var resultType = Nullable.GetUnderlyingType(method.ReturnType) ?? method.ReturnType;
        if (method == Quarter || method == NullableQuarter)
        {
            return sqlExpressionFactory.Function(
                "DATEPART",
                [sqlExpressionFactory.Fragment("quarter"), value],
                nullable: true,
                argumentsPropagateNullability: [false, true],
                resultType,
                typeMappingSource.FindMapping(typeof(int)));
        }

        var format = method == Hour || method == NullableHour
            ? "HH24"
            : method == Minute || method == NullableMinute
                ? "MI"
                : method == IsoWeek || method == NullableIsoWeek
                    ? "IW"
                    : null;

        return format is null
            ? null
            : sqlExpressionFactory.Function(
                "TRUNC",
                [value, sqlExpressionFactory.Constant(format)],
                nullable: true,
                argumentsPropagateNullability: [true, false],
                resultType,
                value.TypeMapping ?? typeMappingSource.FindMapping(typeof(DateTime)));
    }

    private static MethodInfo Find(string name, Type valueType)
        => Functions.GetRuntimeMethod(name, [typeof(DbFunctions), valueType])!;
}
