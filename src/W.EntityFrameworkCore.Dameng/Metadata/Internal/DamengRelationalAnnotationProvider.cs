using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace W.EntityFrameworkCore.Dameng.Metadata.Internal;

internal sealed class DamengRelationalAnnotationProvider(
    RelationalAnnotationProviderDependencies dependencies)
    : RelationalAnnotationProvider(dependencies)
{
    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        if (!designTime)
        {
            yield break;
        }

        var annotations = table.EntityTypeMappings
            .Select(mapping => mapping.TypeBase.FindAnnotation(DamengAnnotationNames.IsClusterBtree))
            .Where(annotation => annotation is not null).ToList();
        if (annotations.Any(annotation => annotation!.Value is not true))
        {
            throw new NotSupportedException("Dameng table storage annotation supports only CLUSTERBTR (true).");
        }

        if (annotations.Count > 0)
        {
            yield return new Annotation(DamengAnnotationNames.IsClusterBtree, true);
        }

        var fillAnnotations = table.EntityTypeMappings
            .Select(mapping => mapping.TypeBase.FindAnnotation(DamengAnnotationNames.TableFillFactor))
            .Where(annotation => annotation is not null).ToList();
        if (fillAnnotations.Count > 0)
        {
            if (fillAnnotations.Any(annotation => annotation!.Value is not int or < 0 or > 100))
                throw new NotSupportedException("Dameng table fill factor must be an integer from 0 to 100.");
            var values = fillAnnotations.Select(annotation => (int)annotation!.Value!).Select(value => value == 0 ? 100 : value).Distinct().ToList();
            if (values.Count != 1)
                throw new NotSupportedException("Mapped entity types must agree on the Dameng table fill factor.");
            yield return new Annotation(DamengAnnotationNames.TableFillFactor, values[0]);
        }
    }

    public override IEnumerable<IAnnotation> For(IUniqueConstraint constraint, bool designTime)
    {
        if (designTime
            && constraint.MappedKeys.FirstOrDefault(key => key.IsPrimaryKey())
                ?.FindAnnotation(DamengAnnotationNames.IsClustered) is { } annotation)
        {
            yield return annotation;
        }
    }

    public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
    {
        if (!designTime)
        {
            yield break;
        }

        var storeObject = StoreObjectIdentifier.Table(
            column.Table.Name,
            column.Table.Schema);
        var property = column.PropertyMappings
            .Select(mapping => mapping.Property)
            .FirstOrDefault(candidate =>
                candidate.GetDamengValueGenerationStrategy(storeObject)
                    != DamengValueGenerationStrategy.None);

        if (property is null)
        {
            yield break;
        }

        var strategy = property.GetDamengValueGenerationStrategy(storeObject);
        yield return new Annotation(DamengAnnotationNames.ValueGenerationStrategy, strategy);

        if (strategy == DamengValueGenerationStrategy.IdentityColumn)
        {
            yield return new Annotation(
                DamengAnnotationNames.IdentitySeed,
                property.GetDamengIdentitySeed());
            yield return new Annotation(
                DamengAnnotationNames.IdentityIncrement,
                property.GetDamengIdentityIncrement());
        }
        else if (strategy == DamengValueGenerationStrategy.Sequence)
        {
            yield return new Annotation(
                DamengAnnotationNames.SequenceName,
                property.GetDamengSequenceName()
                    ?? property.DeclaringType.GetRootType().ShortName() + "Sequence");

            if (property.GetDamengSequenceSchema() is { } sequenceSchema)
            {
                yield return new Annotation(
                    DamengAnnotationNames.SequenceSchema,
                    sequenceSchema);
            }
        }
    }
}
