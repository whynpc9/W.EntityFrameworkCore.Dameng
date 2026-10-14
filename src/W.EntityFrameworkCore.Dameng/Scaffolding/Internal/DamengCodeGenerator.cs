using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Scaffolding.Internal;

/// <summary>
/// Generates provider configuration calls for scaffolded contexts using the selected driver's
/// <c>Use...</c> method.
/// </summary>
internal sealed class DamengCodeGenerator(
    ProviderCodeGeneratorDependencies dependencies,
    DamengDriver driver)
    : ProviderCodeGenerator(dependencies)
{
    /// <inheritdoc />
    public override MethodCallCodeFragment GenerateUseProvider(
        string connectionString,
        MethodCallCodeFragment? providerOptions)
        => new(
            driver.UseProviderMethod,
            providerOptions is null
                ? [connectionString]
                : [connectionString, new NestedClosureCodeFragment("damengOptions", providerOptions)]);
}
