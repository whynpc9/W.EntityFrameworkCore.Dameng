using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Retries operations that fail with a transient Dameng database error.
/// </summary>
/// <remarks>
/// The strategy is scoped to a <see cref="DbContext" /> and is not required to be thread-safe.
/// Constructing it for a context whose Dameng driver does not support automatic replay throws
/// <see cref="NotSupportedException" />.
/// </remarks>
public class DamengRetryingExecutionStrategy : ExecutionStrategy
{
    private readonly FrozenSet<int>? _additionalErrorNumbers;

    /// <summary>
    /// Initializes a strategy using EF Core's default retry count and maximum delay.
    /// </summary>
    /// <param name="context">The context whose operations will be executed.</param>
    public DamengRetryingExecutionStrategy(DbContext context)
        : this(context, DefaultMaxRetryCount)
    {
    }

    /// <summary>
    /// Initializes a strategy using EF Core's default retry count and maximum delay.
    /// </summary>
    /// <param name="dependencies">The execution-strategy dependencies.</param>
    public DamengRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : this(dependencies, DefaultMaxRetryCount)
    {
    }

    /// <summary>
    /// Initializes a strategy using the specified retry count and EF Core's default maximum delay.
    /// </summary>
    /// <param name="context">The context whose operations will be executed.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    public DamengRetryingExecutionStrategy(DbContext context, int maxRetryCount)
        : this(context, maxRetryCount, DefaultMaxDelay, errorNumbersToAdd: null)
    {
    }

    /// <summary>
    /// Initializes a strategy using the specified retry count and EF Core's default maximum delay.
    /// </summary>
    /// <param name="dependencies">The execution-strategy dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    public DamengRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount)
        : this(dependencies, maxRetryCount, DefaultMaxDelay, errorNumbersToAdd: null)
    {
    }

    /// <summary>
    /// Initializes a strategy using EF Core's defaults and additional transient error numbers.
    /// </summary>
    /// <param name="dependencies">The execution-strategy dependencies.</param>
    /// <param name="errorNumbersToAdd">
    /// Additional driver error numbers (for the official driver, <c>Dm.DmException.Number</c>) to treat as transient.
    /// </param>
    public DamengRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        IEnumerable<int> errorNumbersToAdd)
        : this(dependencies, DefaultMaxRetryCount, DefaultMaxDelay, errorNumbersToAdd)
    {
    }

    /// <summary>
    /// Initializes a strategy using the supplied retry settings.
    /// </summary>
    /// <param name="context">The context whose operations will be executed.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    /// <param name="errorNumbersToAdd">
    /// Additional driver error numbers (for the official driver, <c>Dm.DmException.Number</c>) to treat as transient.
    /// </param>
    public DamengRetryingExecutionStrategy(
        DbContext context,
        int maxRetryCount,
        TimeSpan maxRetryDelay,
        IEnumerable<int>? errorNumbersToAdd)
        : base(context, maxRetryCount, maxRetryDelay)
    {
        _additionalErrorNumbers = errorNumbersToAdd?.ToFrozenSet();
        Driver = GetSupportedDriver(Dependencies.Options);
    }

    /// <summary>
    /// Initializes a strategy using the supplied retry settings.
    /// </summary>
    /// <param name="dependencies">The execution-strategy dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    /// <param name="errorNumbersToAdd">
    /// Additional driver error numbers (for the official driver, <c>Dm.DmException.Number</c>) to treat as transient.
    /// </param>
    public DamengRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay,
        IEnumerable<int>? errorNumbersToAdd)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
        _additionalErrorNumbers = errorNumbersToAdd?.ToFrozenSet();
        Driver = GetSupportedDriver(Dependencies.Options);
    }

    /// <summary>
    /// Gets the configured additional transient error numbers.
    /// </summary>
    public virtual IEnumerable<int>? AdditionalErrorNumbers
        => _additionalErrorNumbers;

    private DamengDriver Driver { get; }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception exception)
        => Driver.ShouldRetryOn(exception, _additionalErrorNumbers);

    private static DamengDriver GetSupportedDriver(IDbContextOptions options)
    {
        var driver = options.FindExtension<DamengOptionsExtension>()?.Driver
            ?? DamengOfficialDriver.Instance;

        return driver.SupportsRetryOnFailure
            ? driver
            : throw DamengDriverErrors.RetryNotSupported(driver);
    }
}
