using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Generates Dameng SQL from EF Core relational query expressions.
/// </summary>
internal sealed class DamengQuerySqlGenerator : QuerySqlGenerator
{
    /// <summary>
    /// Initializes a new query SQL generator.
    /// </summary>
    public DamengQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override string GetOperator(SqlBinaryExpression binaryExpression)
        => binaryExpression is { OperatorType: ExpressionType.Add, Type: not null }
            && binaryExpression.Type == typeof(string)
                ? " || "
                : base.GetOperator(binaryExpression);

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression extensionExpression)
    {
        if (extensionExpression is not DamengListAggExpression aggregate)
        {
            return base.VisitExtension(extensionExpression);
        }

        Sql.Append("LISTAGG(");
        Visit(aggregate.Value);
        Sql.Append(", ");
        Visit(aggregate.Separator);
        Sql.Append(")");
        if (aggregate.Orderings.Count > 0)
        {
            Sql.Append(" WITHIN GROUP (ORDER BY ");
            for (var index = 0; index < aggregate.Orderings.Count; index++)
            {
                if (index > 0)
                {
                    Sql.Append(", ");
                }

                Visit(aggregate.Orderings[index]);
            }

            Sql.Append(")");
        }

        return aggregate;
    }

    /// <inheritdoc />
    protected override void GeneratePseudoFromClause()
    {
        // Dameng accepts SELECT statements without a FROM clause.
    }
}
