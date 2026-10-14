using System.Text;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

/// <summary>
/// Internal configuration state used by the Dameng EF Core provider.
/// </summary>
public sealed class DamengOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    private DamengDriver? _driver;
    private bool _retryOnFailureRequested;

    /// <summary>
    /// Initializes an empty provider options extension.
    /// </summary>
    public DamengOptionsExtension()
    {
    }

    private DamengOptionsExtension(DamengOptionsExtension copyFrom)
        : base(copyFrom)
    {
        _driver = copyFrom._driver;
        _retryOnFailureRequested = copyFrom._retryOnFailureRequested;
    }

    /// <inheritdoc />
    public override DbContextOptionsExtensionInfo Info
        => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// Gets the selected driver; options that never selected one use the official driver.
    /// </summary>
    public DamengDriver Driver
        => _driver ?? DamengOfficialDriver.Instance;

    /// <summary>
    /// Gets a value indicating whether the provider's built-in retrying strategy was requested.
    /// </summary>
    public bool RetryOnFailureRequested
        => _retryOnFailureRequested;

    /// <summary>
    /// Creates a copy of this extension that selects the supplied driver.
    /// </summary>
    /// <param name="driver">The driver to select.</param>
    /// <returns>The new extension.</returns>
    /// <exception cref="InvalidOperationException">Another driver is already selected.</exception>
    /// <exception cref="NotSupportedException">
    /// Built-in retries were requested and the driver does not support them.
    /// </exception>
    public DamengOptionsExtension WithDriver(DamengDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        if (_driver is not null && !_driver.Equals(driver))
        {
            throw DamengDriverErrors.ConflictingDriver(_driver, driver);
        }

        if (_retryOnFailureRequested && !driver.SupportsRetryOnFailure)
        {
            throw DamengDriverErrors.RetryNotSupported(driver);
        }

        var clone = (DamengOptionsExtension)Clone();
        clone._driver = driver;
        return clone;
    }

    /// <summary>
    /// Creates a copy of this extension that records a request for the built-in retrying strategy.
    /// </summary>
    /// <returns>The new extension.</returns>
    /// <exception cref="NotSupportedException">The selected driver does not support retries.</exception>
    public DamengOptionsExtension WithRetryOnFailureRequested()
    {
        if (!Driver.SupportsRetryOnFailure)
        {
            throw DamengDriverErrors.RetryNotSupported(Driver);
        }

        var clone = (DamengOptionsExtension)Clone();
        clone._retryOnFailureRequested = true;
        return clone;
    }

    /// <inheritdoc />
    protected override RelationalOptionsExtension Clone()
        => new DamengOptionsExtension(this);

    /// <inheritdoc />
    public override void ApplyServices(IServiceCollection services)
        => DamengDriverConfiguration.AddEntityFrameworkDameng(services, Driver);

    /// <inheritdoc />
    public override void Validate(IDbContextOptions options)
    {
        base.Validate(options);

        if (_retryOnFailureRequested && !Driver.SupportsRetryOnFailure)
        {
            throw DamengDriverErrors.RetryNotSupported(Driver);
        }

        if (Connection is not null && !Driver.IsDriverConnection(Connection))
        {
            throw DamengDriverErrors.ForeignConnection(Driver, Connection);
        }
    }

    private sealed class ExtensionInfo(DamengOptionsExtension extension)
        : RelationalExtensionInfo(extension)
    {
        private string? _logFragment;

        private new DamengOptionsExtension Extension
            => (DamengOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider
            => true;

        // Only the stable driver name is logged; never connection strings, data sources, or users.
        public override string LogFragment
            => _logFragment ??= new StringBuilder(base.LogFragment)
                .Append("UsingDameng Driver=")
                .Append(Extension.Driver.Name)
                .Append(' ')
                .ToString();

        public override int GetServiceProviderHashCode()
            => Extension.Driver.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo
                && otherInfo.Extension.Driver.Equals(Extension.Driver);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["W.EntityFrameworkCore.Dameng"] = "1";
            debugInfo["Dameng:Driver"] = Extension.Driver.Name;
        }
    }
}
