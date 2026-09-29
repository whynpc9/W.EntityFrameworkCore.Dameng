using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Rewrites operations whose normal SQL form is not valid for Dameng LOB values.
/// </summary>
internal sealed class DamengLobQueryRewriter(
    ISqlExpressionFactory sqlExpressionFactory)
    : ExpressionVisitor
{
    protected override Expression VisitExtension(Expression node)
        => node switch
        {
            SqlBinaryExpression binaryExpression
                => VisitSqlBinary(binaryExpression),
            SqlFunctionExpression functionExpression
                => VisitSqlFunction(functionExpression),
            SelectExpression selectExpression
                => VisitSelect(selectExpression),
            SetOperationBase setOperation
                => VisitSetOperation(setOperation),
            _ => base.VisitExtension(node)
        };

    private SqlFunctionExpression VisitSqlFunction(SqlFunctionExpression function)
    {
        var visited = (SqlFunctionExpression)base.VisitExtension(function);
        if (!visited.IsBuiltIn
            || !string.Equals(visited.Name, "COALESCE", StringComparison.OrdinalIgnoreCase)
            || visited.Type != typeof(string)
            || GetLobKind(visited.TypeMapping) != LobKind.Text
            || visited.Arguments is not { Count: > 1 } arguments
            || !arguments.Any(argument => GetLobKind(argument.TypeMapping) == LobKind.Text))
        {
            return visited;
        }

        var lobMapping = visited.TypeMapping!;
        var rewrittenArguments = arguments.Select(argument =>
            argument.Type == typeof(string)
                && (GetLobKind(argument.TypeMapping) != LobKind.Text
                    || argument is SqlConstantExpression)
                ? sqlExpressionFactory.Convert(argument, typeof(string), lobMapping)
                : argument).ToArray();

        // The string literal in COALESCE(lob, '') makes Dameng evaluate a
        // bounded text result. Casting the fallback inside COALESCE keeps the
        // result as a LOB before it participates in concatenation.
        return visited.Update(visited.Instance, rewrittenArguments);
    }

    private SqlExpression VisitSqlBinary(SqlBinaryExpression binary)
    {
        var left = (SqlExpression)Visit(binary.Left);
        var right = (SqlExpression)Visit(binary.Right);
        var updated = binary.Update(left, right);

        if (binary.OperatorType is not (ExpressionType.Equal or ExpressionType.NotEqual)
            || !TryGetLobEqualityFunction(left, right, out var functionName))
        {
            return updated;
        }

        var equalityResult = sqlExpressionFactory.ApplyDefaultTypeMapping(
            sqlExpressionFactory.Function(
                functionName,
                [left, right],
                nullable: false,
                argumentsPropagateNullability: [false, false],
                typeof(int),
                typeMapping: null));

        var nullResult = sqlExpressionFactory.Constant(
            value: null,
            typeof(int),
            equalityResult.TypeMapping);
        equalityResult = sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    sqlExpressionFactory.OrElse(
                        sqlExpressionFactory.IsNull(left),
                        sqlExpressionFactory.IsNull(right)),
                    nullResult)
            ],
            equalityResult);

        return sqlExpressionFactory.Equal(
            equalityResult,
            sqlExpressionFactory.Constant(
                binary.OperatorType == ExpressionType.Equal ? 1 : 0,
                equalityResult.TypeMapping));
    }

    private SelectExpression VisitSelect(SelectExpression select)
    {
        var visited = (SelectExpression)base.VisitExtension(select);

        ThrowIfContainsLob(
            visited.GroupBy,
            "GROUP BY");
        ThrowIfContainsLob(
            visited.Orderings.Select(ordering => ordering.Expression),
            "ORDER BY");

        if (visited.IsDistinct)
        {
            ThrowIfContainsLob(
                visited.Projection.Select(projection => projection.Expression),
                "SELECT DISTINCT");
        }

        return visited;
    }

    private SetOperationBase VisitSetOperation(SetOperationBase setOperation)
    {
        var visited = (SetOperationBase)base.VisitExtension(setOperation);

        if (visited.IsDistinct)
        {
            ThrowIfContainsLob(
                visited.Source1.Projection.Select(projection => projection.Expression),
                visited switch
                {
                    UnionExpression => "UNION",
                    IntersectExpression => "INTERSECT",
                    ExceptExpression => "EXCEPT",
                    _ => "set operation"
                });
        }

        return visited;
    }

    private static void ThrowIfContainsLob(
        IEnumerable<SqlExpression> expressions,
        string operation)
    {
        var lob = expressions.FirstOrDefault(IsLob);
        if (lob is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Dameng cannot apply {operation} to the LOB store type "
            + $"'{lob.TypeMapping!.StoreType}' with portable server semantics. "
            + "Configure a bounded inline store type (for example with HasMaxLength) "
            + "or explicitly convert the value to a bounded type before this operation.");
    }

    private static bool TryGetLobEqualityFunction(
        SqlExpression left,
        SqlExpression right,
        out string functionName)
    {
        var leftKind = GetLobKind(left.TypeMapping);
        var rightKind = GetLobKind(right.TypeMapping);
        var kind = leftKind ?? rightKind;

        if (kind is null
            || leftKind is not null && rightKind is not null && leftKind != rightKind)
        {
            functionName = null!;
            return false;
        }

        functionName = kind == LobKind.Binary
            ? "BLOB_EQUAL"
            : "TEXT_EQUAL";
        return true;
    }

    private static bool IsLob(SqlExpression expression)
        => GetLobKind(expression.TypeMapping) is not null;

    private static LobKind? GetLobKind(RelationalTypeMapping? typeMapping)
        => typeMapping?.StoreTypeNameBase.ToUpperInvariant() switch
        {
            "CLOB" or "NCLOB" or "TEXT" or "NTEXT" or "LONG" or "LONGVARCHAR"
                => LobKind.Text,
            "BLOB" or "IMAGE" or "LONGVARBINARY" or "BFILE"
                => LobKind.Binary,
            _ => null
        };

    private enum LobKind
    {
        Text,
        Binary
    }
}
