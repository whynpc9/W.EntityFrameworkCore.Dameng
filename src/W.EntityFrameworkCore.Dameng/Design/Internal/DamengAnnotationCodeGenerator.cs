using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;

namespace W.EntityFrameworkCore.Dameng.Design.Internal;

/// <summary>
/// Converts Dameng annotations into calls to the provider's public fluent APIs.
/// </summary>
internal sealed class DamengAnnotationCodeGenerator(
    AnnotationCodeGeneratorDependencies dependencies)
    : AnnotationCodeGenerator(dependencies)
{
    private static readonly MethodInfo ModelUseIdentityColumnsMethod
        = typeof(DamengModelBuilderExtensions).GetRuntimeMethod(
            nameof(DamengModelBuilderExtensions.UseDamengIdentityColumns),
            [typeof(ModelBuilder), typeof(long), typeof(int)])!;

    private static readonly MethodInfo PropertyUseIdentityColumnMethod
        = typeof(DamengPropertyBuilderExtensions).GetRuntimeMethod(
            nameof(DamengPropertyBuilderExtensions.UseDamengIdentityColumn),
            [typeof(PropertyBuilder), typeof(long), typeof(int)])!;

    private static readonly MethodInfo PropertyUseSequenceMethod
        = typeof(DamengPropertyBuilderExtensions).GetRuntimeMethod(
            nameof(DamengPropertyBuilderExtensions.UseDamengSequence),
            [typeof(PropertyBuilder), typeof(string), typeof(string)])!;

    /// <inheritdoc />
    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IModel model,
        IDictionary<string, IAnnotation> annotations)
    {
        var damengCall = GenerateModelValueGenerationCall(annotations);
        var calls = new List<MethodCallCodeFragment>(
            base.GenerateFluentApiCalls(model, annotations));

        if (damengCall is not null)
        {
            calls.Add(damengCall);
        }

        return calls;
    }

    /// <inheritdoc />
    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IProperty property,
        IDictionary<string, IAnnotation> annotations)
    {
        var damengCall = GeneratePropertyValueGenerationCall(property, annotations);
        var calls = new List<MethodCallCodeFragment>(
            base.GenerateFluentApiCalls(property, annotations));

        if (damengCall is not null)
        {
            calls.Add(damengCall);
        }

        return calls;
    }

    private static MethodCallCodeFragment? GenerateModelValueGenerationCall(
        IDictionary<string, IAnnotation> annotations)
    {
        if (!TryGetStrategy(annotations, out var strategy)
            || strategy != DamengValueGenerationStrategy.IdentityColumn)
        {
            return null;
        }

        annotations.Remove(DamengAnnotationNames.ValueGenerationStrategy);
        var seed = GetIdentitySeedAndRemove(annotations) ?? 1L;
        var increment = GetIntAndRemove(annotations, DamengAnnotationNames.IdentityIncrement) ?? 1;

        return new MethodCallCodeFragment(
            ModelUseIdentityColumnsMethod,
            IdentityArguments(seed, increment));
    }

    private static MethodCallCodeFragment? GeneratePropertyValueGenerationCall(
        IProperty property,
        IDictionary<string, IAnnotation> annotations)
    {
        if (!TryGetStrategy(annotations, out var strategy))
        {
            return null;
        }

        switch (strategy)
        {
            case DamengValueGenerationStrategy.IdentityColumn:
                annotations.Remove(DamengAnnotationNames.ValueGenerationStrategy);
                // The generated call writes both facets to the property, including omitted arguments.
                var seed = GetIdentitySeedAndRemove(annotations) ?? property.GetDamengIdentitySeed();
                var increment = GetIntAndRemove(annotations, DamengAnnotationNames.IdentityIncrement)
                    ?? property.GetDamengIdentityIncrement();
                return new MethodCallCodeFragment(
                    PropertyUseIdentityColumnMethod,
                    IdentityArguments(seed, increment));

            case DamengValueGenerationStrategy.Sequence:
                annotations.Remove(DamengAnnotationNames.ValueGenerationStrategy);
                var name = GetAndRemove<string>(annotations, DamengAnnotationNames.SequenceName);
                var schema = GetAndRemove<string>(annotations, DamengAnnotationNames.SequenceSchema);

                return new MethodCallCodeFragment(
                    PropertyUseSequenceMethod,
                    SequenceArguments(name, schema));

            default:
                // No public fluent API represents an explicit "None"; preserve the raw annotation.
                return null;
        }
    }

    private static bool TryGetStrategy(
        IDictionary<string, IAnnotation> annotations,
        out DamengValueGenerationStrategy strategy)
    {
        if (annotations.TryGetValue(DamengAnnotationNames.ValueGenerationStrategy, out var annotation)
            && annotation.Value is DamengValueGenerationStrategy value)
        {
            strategy = value;
            return true;
        }

        strategy = default;
        return false;
    }

    private static long? GetIdentitySeedAndRemove(
        IDictionary<string, IAnnotation> annotations)
    {
        if (!annotations.Remove(DamengAnnotationNames.IdentitySeed, out var annotation)
            || annotation.Value is null)
        {
            return null;
        }

        return annotation.Value switch
        {
            int value => value,
            long value => value,
            _ => Convert.ToInt64(annotation.Value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static T? GetAndRemove<T>(
        IDictionary<string, IAnnotation> annotations,
        string name)
        where T : class
    {
        if (annotations.Remove(name, out var annotation)
            && annotation.Value is T value)
        {
            return value;
        }

        return default;
    }

    private static int? GetIntAndRemove(
        IDictionary<string, IAnnotation> annotations,
        string name)
    {
        if (annotations.Remove(name, out var annotation)
            && annotation.Value is int value)
        {
            return value;
        }

        return null;
    }

    private static object?[] IdentityArguments(long seed, int increment)
        => (seed, increment) switch
        {
            (1L, 1) => [],
            (_, 1) => [seed],
            _ => [seed, increment]
        };

    private static object?[] SequenceArguments(string? name, string? schema)
        => (name, schema) switch
        {
            (null, null) => [],
            (_, null) => [name],
            _ => [name, schema]
        };
}
