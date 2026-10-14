using Microsoft.EntityFrameworkCore.Query;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengSqlTranslatingExpressionVisitorFactory(
    RelationalSqlTranslatingExpressionVisitorDependencies dependencies)
    : RelationalSqlTranslatingExpressionVisitorFactory(dependencies)
{
    public override RelationalSqlTranslatingExpressionVisitor Create(
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
        => new DamengSqlTranslatingExpressionVisitor(Dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor);
}
