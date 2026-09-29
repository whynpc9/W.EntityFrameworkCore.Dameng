using Microsoft.EntityFrameworkCore.Query;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Registers Dameng-specific grouped aggregate translations.
/// </summary>
internal sealed class DamengAggregateMethodCallTranslatorProvider
    : RelationalAggregateMethodCallTranslatorProvider
{
    public DamengAggregateMethodCallTranslatorProvider(
        RelationalAggregateMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
        => AddTranslators(
        [
            new DamengStringAggregateMethodTranslator(
                dependencies.SqlExpressionFactory,
                dependencies.RelationalTypeMappingSource)
        ]);
}
