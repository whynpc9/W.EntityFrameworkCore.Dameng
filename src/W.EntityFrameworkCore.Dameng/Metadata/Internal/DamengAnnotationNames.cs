namespace W.EntityFrameworkCore.Dameng.Metadata.Internal;

internal static class DamengAnnotationNames
{
    public const string Prefix = "Dameng:";

    public const string ValueGenerationStrategy = Prefix + nameof(ValueGenerationStrategy);

    public const string IdentitySeed = Prefix + nameof(IdentitySeed);

    public const string IdentityIncrement = Prefix + nameof(IdentityIncrement);

    public const string SequenceName = Prefix + nameof(SequenceName);

    public const string SequenceSchema = Prefix + nameof(SequenceSchema);

    public const string IsClustered = Prefix + nameof(IsClustered);

    public const string IsClusterBtree = Prefix + nameof(IsClusterBtree);
    public const string TableFillFactor = Prefix + nameof(TableFillFactor);
}
