using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection registration methods for the Dameng EF Core provider.
/// </summary>
public static class DamengServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services used by the Dameng EF Core provider with the official <c>DM.DmProvider</c> driver.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection so additional calls can be chained.</returns>
    public static IServiceCollection AddEntityFrameworkDameng(this IServiceCollection services)
        => DamengDriverConfiguration.AddEntityFrameworkDameng(services, DamengOfficialDriver.Instance);
}
