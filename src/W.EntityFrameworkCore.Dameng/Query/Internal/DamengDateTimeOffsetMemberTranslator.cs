using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengDateTimeOffsetMemberTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource)
    : IMemberTranslator
{
    private static readonly MemberInfo Year = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Year))!;
    private static readonly MemberInfo Month = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Month))!;
    private static readonly MemberInfo Day = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Day))!;
    private static readonly MemberInfo Hour = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Hour))!;
    private static readonly MemberInfo Minute = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Minute))!;
    private static readonly MemberInfo Second = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Second))!;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || member.DeclaringType != typeof(DateTimeOffset) || returnType != typeof(int))
        {
            return null;
        }

        var part = member == Year
            ? "year"
            : member == Month
                ? "month"
                : member == Day
                    ? "day"
                    : member == Hour
                        ? "hour"
                        : member == Minute
                            ? "minute"
                            : member == Second
                                ? "second"
                                : null;

        return part is null
            ? null
            : sqlExpressionFactory.Function(
                "DATEPART",
                [sqlExpressionFactory.Fragment(part), instance],
                nullable: true,
                argumentsPropagateNullability: [false, true],
                typeof(int),
                typeMappingSource.FindMapping(typeof(int)));
    }
}
