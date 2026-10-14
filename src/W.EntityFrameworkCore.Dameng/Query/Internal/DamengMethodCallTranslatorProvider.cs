using Microsoft.EntityFrameworkCore.Query;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Provides Dameng translations for CLR method calls.
/// </summary>
internal sealed class DamengMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    /// <summary>
    /// Initializes a new method-call translator provider.
    /// </summary>
    public DamengMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
        => AddTranslators(
        [
            new DamengDateTimeMethodTranslator(dependencies.SqlExpressionFactory),
            new DamengDateTimeToStringTranslator(
                dependencies.SqlExpressionFactory,
                dependencies.RelationalTypeMappingSource),
            new DamengDateBucketMethodTranslator(
                dependencies.SqlExpressionFactory,
                dependencies.RelationalTypeMappingSource),
            new DamengMathTranslator(dependencies.SqlExpressionFactory),
            new DamengStringMethodTranslator(dependencies.SqlExpressionFactory),
            new DamengNewGuidTranslator(
                dependencies.SqlExpressionFactory,
                dependencies.RelationalTypeMappingSource)
        ]);
}
