using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Provides Dameng translations for CLR members.
/// </summary>
internal sealed class DamengMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    /// <summary>
    /// Initializes a new member translator provider.
    /// </summary>
    public DamengMemberTranslatorProvider(
        RelationalMemberTranslatorProviderDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource)
        : base(dependencies)
        => AddTranslators(
        [
            new DamengDateTimeMemberTranslator(dependencies.SqlExpressionFactory),
            new DamengDateTimeOffsetMemberTranslator(
                dependencies.SqlExpressionFactory,
                typeMappingSource),
            new DamengStringMemberTranslator(dependencies.SqlExpressionFactory)
        ]);
}
