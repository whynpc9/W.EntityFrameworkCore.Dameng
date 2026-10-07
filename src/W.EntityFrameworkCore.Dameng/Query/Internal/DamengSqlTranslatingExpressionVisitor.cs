using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengSqlTranslatingExpressionVisitor(
    RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
    QueryCompilationContext queryCompilationContext,
    QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
    : RelationalSqlTranslatingExpressionVisitor(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
{
    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    {
        // Relational translation removes boxing conversions before method translators
        // receive their SQL operand. Preserve the distinction from Nullable<T>.ToString:
        // boxing null and calling object.ToString throws instead of returning empty text.
        if (methodCallExpression.Method.DeclaringType == typeof(object)
            && methodCallExpression.Method.Name == nameof(object.ToString)
            && methodCallExpression.Arguments.Count == 0
            && methodCallExpression.Object is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs,
                Type: var targetType,
                Operand: var operand
            }
            && targetType == typeof(object)
            && (Nullable.GetUnderlyingType(operand.Type) ?? operand.Type) == typeof(DateTime))
        {
            AddTranslationErrorDetails("Dameng does not translate boxed DateTime.ToString calls.");
            return QueryCompilationContext.NotTranslatedExpression;
        }

        return base.VisitMethodCall(methodCallExpression);
    }
}
