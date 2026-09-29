using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Applies Dameng-specific SQL-tree processing after parameter expansion.
/// </summary>
internal sealed class DamengParameterBasedSqlProcessor(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
    RelationalParameterBasedSqlProcessorParameters parameters)
    : RelationalParameterBasedSqlProcessor(dependencies, parameters)
{
    protected override Expression ProcessSqlNullability(
        Expression queryExpression,
        ParametersCacheDecorator parametersDecorator)
        => new DamengSqlNullabilityProcessor(Dependencies, Parameters)
            .Process(queryExpression, parametersDecorator);

    public override Expression Process(
        Expression queryExpression,
        ParametersCacheDecorator parametersDecorator)
    {
        var processed = base.Process(queryExpression, parametersDecorator);
        processed = new DamengLobQueryRewriter(
                Dependencies.SqlExpressionFactory)
            .Visit(processed);

        return new DamengSearchConditionConverter(
                Dependencies.SqlExpressionFactory)
            .Visit(processed);
    }
}
