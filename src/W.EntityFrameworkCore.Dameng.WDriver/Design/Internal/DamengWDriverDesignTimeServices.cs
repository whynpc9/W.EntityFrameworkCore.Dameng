using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.WDriver.Infrastructure.Internal;

[assembly: DesignTimeProviderServices(
    "W.EntityFrameworkCore.Dameng.WDriver.Design.Internal.DamengWDriverDesignTimeServices")]

namespace W.EntityFrameworkCore.Dameng.WDriver.Design.Internal;

/// <summary>
/// Registers the services used by Entity Framework Core tools for the Dameng provider with the
/// <c>W.DamengProvider</c> driver.
/// </summary>
public sealed class DamengWDriverDesignTimeServices : IDesignTimeServices
{
    /// <inheritdoc />
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
        => DamengDriverConfiguration.AddDesignTimeServices(serviceCollection, DamengWDriver.Instance);
}
