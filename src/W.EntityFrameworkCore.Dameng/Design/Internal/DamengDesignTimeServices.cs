using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

[assembly: DesignTimeProviderServices(
    "W.EntityFrameworkCore.Dameng.Design.Internal.DamengDesignTimeServices")]

namespace W.EntityFrameworkCore.Dameng.Design.Internal;

/// <summary>
/// Registers the services used by Entity Framework Core tools for the Dameng provider with the
/// official <c>DM.DmProvider</c> driver.
/// </summary>
public sealed class DamengDesignTimeServices : IDesignTimeServices
{
    /// <inheritdoc />
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
        => DamengDriverConfiguration.AddDesignTimeServices(serviceCollection, DamengOfficialDriver.Instance);
}
