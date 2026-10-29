using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengDateTimeMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
    : IMemberTranslator
{
    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member.DeclaringType != typeof(DateTime))
        {
            return null;
        }

        return member.Name switch
        {
            nameof(DateTime.Year) => DatePart("year"),
            nameof(DateTime.Month) => DatePart("month"),
            nameof(DateTime.DayOfYear) => DatePart("dayofyear"),
            nameof(DateTime.Day) => DatePart("day"),
            nameof(DateTime.Hour) => DatePart("hour"),
            nameof(DateTime.Minute) => DatePart("minute"),
            nameof(DateTime.Second) => DatePart("second"),
            nameof(DateTime.Millisecond) => DatePart("millisecond"),
            nameof(DateTime.Microsecond) => sqlExpressionFactory.Modulo(
                DatePart("microsecond"),
                sqlExpressionFactory.Constant(1000)),
            nameof(DateTime.Date) => Truncate(instance!),
            nameof(DateTime.Now) => LocalTimestamp(),
            nameof(DateTime.UtcNow) => UtcTimestamp(),
            nameof(DateTime.Today) => Truncate(LocalTimestamp()),
            _ => null
        };

        SqlExpression DatePart(string part)
        {
            DamengTemporalTranslationGuard.EnsureNative(instance!);
            return sqlExpressionFactory.Function(
                "DATEPART",
                [sqlExpressionFactory.Fragment(part), instance!],
                nullable: true,
                argumentsPropagateNullability: [false, true],
                returnType);
        }

        SqlExpression Truncate(SqlExpression value)
        {
            DamengTemporalTranslationGuard.EnsureNative(value);
            return sqlExpressionFactory.Function(
                "TRUNC",
                [value],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(DateTime),
                value.TypeMapping);
        }

        SqlExpression LocalTimestamp()
            => sqlExpressionFactory.Function(
                "LOCALTIMESTAMP",
                arguments: [],
                nullable: false,
                argumentsPropagateNullability: [],
                typeof(DateTime));

        SqlExpression UtcTimestamp()
            => sqlExpressionFactory.Function(
                "SYS_EXTRACT_UTC",
                [
                    sqlExpressionFactory.Function(
                        "CURRENT_TIMESTAMP",
                        arguments: [],
                        nullable: false,
                        argumentsPropagateNullability: [],
                        typeof(DateTime))
                ],
                nullable: false,
                argumentsPropagateNullability: [false],
                typeof(DateTime));
    }
}
