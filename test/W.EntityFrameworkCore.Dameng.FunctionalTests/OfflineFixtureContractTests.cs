using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Category", "OfflineFixture")]
public sealed class OfflineFixtureContractTests
{
    [Fact]
    public void OfflineFixturesUseOnlyOrdinaryTestsWithoutDatabaseFixtures()
        => Assert.Empty(FindViolations(typeof(OfflineFixtureContractTests).Assembly.GetTypes()));

    [Theory]
    [InlineData("database")]
    [InlineData("collection")]
    [InlineData("fixture")]
    public void SyntheticInvalidMetadataIsRejectedWithoutConstructingAttributes(string kind)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OfflineMetadata" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("fixture").DefineType("SyntheticFixture", TypeAttributes.Public);
        type.SetCustomAttribute(new CustomAttributeBuilder(typeof(TraitAttribute).GetConstructor([typeof(string), typeof(string)])!, ["Category", "OfflineFixture"]));
        if (kind == "collection")
            type.SetCustomAttribute(new CustomAttributeBuilder(typeof(CollectionAttribute).GetConstructor([typeof(string)])!, ["synthetic"]));
        if (kind == "fixture") type.AddInterfaceImplementation(typeof(IClassFixture<object>));
        var method = type.DefineMethod("Test", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var attribute = kind == "database" ? typeof(DamengFactAttribute) : typeof(FactAttribute);
        method.SetCustomAttribute(new CustomAttributeBuilder(attribute.GetConstructor(Type.EmptyTypes)!, []));
        Assert.NotEmpty(FindViolations([type.CreateType()!]));
    }

    private static List<string> FindViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();
        foreach (var type in types)
        {
            var typeAttributes = TypeAttributesIncludingBases(type).ToArray();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attributes = method.GetCustomAttributesData();
                if (!IsOffline(typeAttributes) && !IsOffline(attributes)) continue;
                var testAttributes = attributes.Where(attribute => typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)).ToArray();
                if (testAttributes.Length == 0) continue;
                if (testAttributes.Any(attribute => attribute.AttributeType != typeof(FactAttribute) && attribute.AttributeType != typeof(TheoryAttribute)))
                    violations.Add(type.Name + "." + method.Name + ": database/custom test attribute forbidden.");
                if (typeAttributes.Any(attribute => attribute.AttributeType == typeof(CollectionAttribute)))
                    violations.Add(type.Name + ": collection binding forbidden.");
                if (type.GetInterfaces().Any(contract => contract.IsGenericType
                    && (contract.GetGenericTypeDefinition() == typeof(IClassFixture<>) || contract.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))))
                    violations.Add(type.Name + ": fixture binding forbidden.");
            }
        }
        return violations;
    }

    private static bool IsOffline(IEnumerable<CustomAttributeData> attributes)
        => attributes.Any(attribute => attribute.AttributeType == typeof(TraitAttribute)
            && attribute.ConstructorArguments.Count == 2
            && (string?)attribute.ConstructorArguments[0].Value == "Category"
            && (string?)attribute.ConstructorArguments[1].Value == "OfflineFixture");

    private static IEnumerable<CustomAttributeData> TypeAttributesIncludingBases(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            foreach (var attribute in current.GetCustomAttributesData()) yield return attribute;
    }
}
