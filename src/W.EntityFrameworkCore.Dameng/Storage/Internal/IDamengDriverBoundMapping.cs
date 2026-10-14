using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

/// <summary>
/// A type mapping whose parameter or reader behavior depends on the Dameng driver.
/// </summary>
/// <remarks>
/// The type mapping source keeps driver-free templates and binds every mapping it returns to its
/// own driver. Clones keep the bound driver, so facet and converter variants stay on that driver.
/// </remarks>
internal interface IDamengDriverBoundMapping
{
    DamengDriver? Driver { get; }

    RelationalTypeMapping WithDriver(DamengDriver driver);
}

internal static class DamengDriverBoundMapping
{
    public static DamengDriver RequireDriver(this IDamengDriverBoundMapping mapping)
        => mapping.Driver ?? throw Infrastructure.Internal.DamengDriverErrors.UnboundMapping(mapping.GetType().Name);
}
