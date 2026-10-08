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
        // Preserve the original CLR receiver type before SQL translation erases
        // object-valued branches and boxing. Value-type receivers must still reach
        // the existing enum/scalar translators; reference-typed boxing cannot use
        // the Nullable<DateTime> empty-string conversion contract.
        var receiverType = methodCallExpression.Object?.Type;
        if (methodCallExpression.Method.DeclaringType == typeof(object)
            && methodCallExpression.Method.Name == nameof(object.ToString)
            && methodCallExpression.Arguments.Count == 0
            && receiverType is not null
            && !receiverType.IsValueType
            && receiverType != typeof(string))
        {
            AddTranslationErrorDetails("Dameng does not translate boxed DateTime.ToString or object-valued ToString calls.");
            return QueryCompilationContext.NotTranslatedExpression;
        }

        return base.VisitMethodCall(methodCallExpression);
    }
}
