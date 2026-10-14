using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

/// <summary>
/// One registration of a driver's services; several registrations of the same driver are allowed.
/// </summary>
internal sealed record DamengDriverRegistration(DamengDriver Driver);

/// <summary>
/// Reports the registered driver's provider name and only claims options that select that driver.
/// </summary>
/// <remarks>
/// EF Core's default <see cref="DatabaseProvider{TOptionsExtension}" /> derives its name from the
/// extension's assembly and only checks that the shared extension exists, which cannot tell the
/// drivers apart.
/// </remarks>
internal sealed class DamengDatabaseProvider(
    DatabaseProviderDependencies dependencies,
    DamengDriver driver)
    : DatabaseProvider<DamengOptionsExtension>(dependencies)
{
    public override string Name
        => driver.ProviderName;

    public override bool IsConfigured(IDbContextOptions options)
        => options.FindExtension<DamengOptionsExtension>() is { } extension
            && extension.Driver.Equals(driver);
}

/// <summary>
/// Rejects internal service providers that mix driver registrations or do not match the options,
/// both when the provider is first initialized and every time it is reused.
/// </summary>
internal sealed class DamengDriverSingletonOptions(
    DamengDriver driver,
    IEnumerable<DamengDriverRegistration> registrations)
    : ISingletonOptions
{
    public void Initialize(IDbContextOptions options)
        => Validate(options);

    public void Validate(IDbContextOptions options)
    {
        var registeredDrivers = registrations
            .Select(registration => registration.Driver)
            .Append(driver)
            .Distinct()
            .ToList();
        if (registeredDrivers.Count > 1)
        {
            throw DamengDriverErrors.ConflictingServices(registeredDrivers);
        }

        var configured = options.FindExtension<DamengOptionsExtension>()?.Driver;
        if (configured is not null && !configured.Equals(driver))
        {
            throw DamengDriverErrors.MismatchedServices(driver, configured);
        }
    }
}
