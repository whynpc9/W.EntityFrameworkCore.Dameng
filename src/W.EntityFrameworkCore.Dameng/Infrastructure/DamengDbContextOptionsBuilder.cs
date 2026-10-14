using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

namespace Microsoft.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Configures provider-specific options for the Dameng EF Core provider.
/// </summary>
public sealed class DamengDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<DamengDbContextOptionsBuilder, DamengOptionsExtension>
{
    /// <summary>
    /// Initializes a new instance for the supplied EF Core options builder.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    public DamengDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }

    /// <summary>
    /// Configures the context to retry operations that fail with a transient Dameng error.
    /// </summary>
    /// <remarks>
    /// Uses the EF Core defaults of 6 retry attempts and a maximum delay of 30 seconds.
    /// Only drivers that support automatic replay accept this call; the others throw
    /// <see cref="NotSupportedException" /> instead of silently running without retries.
    /// </remarks>
    /// <returns>The same builder so additional calls can be chained.</returns>
    public DamengDbContextOptionsBuilder EnableRetryOnFailure()
        => EnableBuiltInRetries(dependencies => new DamengRetryingExecutionStrategy(dependencies));

    /// <summary>
    /// Configures the context to retry operations that fail with a transient Dameng error.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <returns>The same builder so additional calls can be chained.</returns>
    public DamengDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount)
        => EnableBuiltInRetries(
            dependencies => new DamengRetryingExecutionStrategy(dependencies, maxRetryCount));

    /// <summary>
    /// Configures the context to retry operations that fail with a transient Dameng error.
    /// </summary>
    /// <param name="errorNumbersToAdd">
    /// Additional driver error numbers (for the official driver, <c>Dm.DmException.Number</c>) to treat as transient.
    /// </param>
    /// <returns>The same builder so additional calls can be chained.</returns>
    public DamengDbContextOptionsBuilder EnableRetryOnFailure(ICollection<int> errorNumbersToAdd)
    {
        ArgumentNullException.ThrowIfNull(errorNumbersToAdd);
        var errorNumbers = errorNumbersToAdd.ToArray();

        return EnableBuiltInRetries(
            dependencies => new DamengRetryingExecutionStrategy(dependencies, errorNumbers));
    }

    /// <summary>
    /// Configures the context to retry operations that fail with a transient Dameng error.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    /// <param name="errorNumbersToAdd">
    /// Additional driver error numbers (for the official driver, <c>Dm.DmException.Number</c>) to treat as transient.
    /// </param>
    /// <returns>The same builder so additional calls can be chained.</returns>
    public DamengDbContextOptionsBuilder EnableRetryOnFailure(
        int maxRetryCount,
        TimeSpan maxRetryDelay,
        IEnumerable<int>? errorNumbersToAdd)
    {
        var errorNumbers = errorNumbersToAdd?.ToArray();

        return EnableBuiltInRetries(
            dependencies => new DamengRetryingExecutionStrategy(
                dependencies,
                maxRetryCount,
                maxRetryDelay,
                errorNumbers));
    }

    // Records the request on the options so a later driver selection or Validate can still
    // reject it when retries were configured before the driver was chosen.
    private DamengDbContextOptionsBuilder EnableBuiltInRetries(
        Func<ExecutionStrategyDependencies, IExecutionStrategy> getExecutionStrategy)
    {
        WithOption(extension => extension.WithRetryOnFailureRequested());
        return ExecutionStrategy(getExecutionStrategy);
    }
}
