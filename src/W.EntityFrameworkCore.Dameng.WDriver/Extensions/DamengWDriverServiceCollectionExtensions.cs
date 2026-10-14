using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.WDriver.Infrastructure.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection registration methods for the Dameng EF Core provider on <c>W.DamengProvider</c>.
/// </summary>
public static class DamengWDriverServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services used by the Dameng EF Core provider with the <c>W.DamengProvider</c> driver.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection so additional calls can be chained.</returns>
    /// <remarks>
    /// Do not also call <c>AddEntityFrameworkDameng</c> on the same collection; services for two
    /// drivers in one internal service provider are rejected.
    /// </remarks>
    public static IServiceCollection AddEntityFrameworkDamengWithWDriver(this IServiceCollection services)
        => DamengDriverConfiguration.AddEntityFrameworkDameng(services, DamengWDriver.Instance);
}
