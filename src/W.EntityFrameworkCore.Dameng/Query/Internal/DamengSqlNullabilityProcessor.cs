using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Visits every child of the ordered LISTAGG expression during parameter-aware null processing.
/// </summary>
internal sealed class DamengSqlNullabilityProcessor(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
    RelationalParameterBasedSqlProcessorParameters parameters)
    : SqlNullabilityProcessor(dependencies, parameters)
{
    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {
        if (sqlExpression is not DamengListAggExpression aggregate)
        {
            return base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable);
        }

        // LISTAGG returns NULL for an empty group or when all input values are skipped.
        nullable = true;
        var value = Visit(aggregate.Value, out _);
        var separator = Visit(aggregate.Separator, out _);
        var orderings = aggregate.Orderings
            .Select(ordering => ordering.Update(Visit(ordering.Expression, out _)))
            .ToArray();
        return aggregate.Update(value, separator, orderings);
    }
}
