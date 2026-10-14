using System.Data.Common;
using System.Reflection;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

namespace W.EntityFrameworkCore.Dameng.Infrastructure;

/// <summary>
/// Provider-specific parameter types that the shared Dameng type mappings ask a driver to set.
/// </summary>
public enum DamengParameterType
{
    /// <summary>A binary large object.</summary>
    Blob,

    /// <summary>A character large object.</summary>
    Clob,

    /// <summary>A fixed-length binary value.</summary>
    Binary,

    /// <summary>A variable-length binary value.</summary>
    VarBinary,

    /// <summary>An <c>INTERVAL DAY TO SECOND</c> value.</summary>
    IntervalDayToSecond
}

/// <summary>
/// Describes and adapts one Dameng ADO.NET driver for the shared EF Core provider services.
/// </summary>
/// <remarks>
/// This is infrastructure for Dameng driver packages, not an application API. Implementations
/// must be stateless singletons: they must not hold a context, connection, transaction, reader,
/// credential, data source, or server capability. Two drivers are equal when they have the same
/// implementation type.
/// </remarks>
public abstract class DamengDriver : IEquatable<DamengDriver>
{
    /// <summary>
    /// Initializes a driver descriptor.
    /// </summary>
    protected DamengDriver()
    {
    }

    /// <summary>
    /// Gets the stable short driver name used in logs, debug information, and error messages.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the EF Core provider name reported by <c>Database.ProviderName</c>.
    /// </summary>
    /// <remarks>
    /// EF Core tools load the assembly with this name to find its design-time services, so it must
    /// be the name of the assembly that carries the driver's design-time provider attribute.
    /// </remarks>
    public abstract string ProviderName { get; }

    /// <summary>
    /// Gets the options-builder method that scaffolded code calls to select this driver.
    /// </summary>
    /// <remarks>
    /// The method must accept a <c>DbContextOptionsBuilder</c>, a connection string, and an optional
    /// <c>Action&lt;DamengDbContextOptionsBuilder&gt;</c>, in that order.
    /// </remarks>
    public abstract MethodInfo UseProviderMethod { get; }

    /// <summary>
    /// Gets a value indicating whether the provider's built-in retrying execution strategy may be
    /// enabled with this driver.
    /// </summary>
    public virtual bool SupportsRetryOnFailure
        => false;

    /// <summary>
    /// Gets a value indicating whether <see cref="DateTimeOffset" /> and <see cref="TimeSpan" />
    /// interval columns are materialized with the driver's typed getters instead of the
    /// provider's text parsing fallback.
    /// </summary>
    public virtual bool UsesTypedTemporalReaders
        => false;

    /// <summary>
    /// Creates an unopened connection for the supplied connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The new connection.</returns>
    public abstract DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Returns whether the supplied connection is a connection of this driver.
    /// </summary>
    /// <param name="connection">The connection to inspect.</param>
    /// <returns><see langword="true" /> when the connection can be used with this driver.</returns>
    public abstract bool IsDriverConnection(DbConnection connection);

    /// <summary>
    /// Sets the driver-specific type of a parameter created by the shared type mappings.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="type">The provider type to set.</param>
    /// <exception cref="InvalidOperationException">
    /// The parameter was not created by this driver.
    /// </exception>
    public abstract void SetParameterType(DbParameter parameter, DamengParameterType type);

    /// <summary>
    /// Applies the mapping's fractional-second precision to a temporal parameter after EF Core
    /// configured it.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="fractionalSecondPrecision">The mapping's effective fractional-second digits.</param>
    public virtual void ConfigureTemporalParameter(DbParameter parameter, int fractionalSecondPrecision)
    {
    }

    /// <summary>
    /// Returns whether the built-in retrying execution strategy should retry after the exception.
    /// </summary>
    /// <param name="exception">The exception thrown by the operation.</param>
    /// <param name="additionalErrorNumbers">User-configured error numbers to treat as transient.</param>
    /// <returns><see langword="true" /> when the operation may be retried.</returns>
    public virtual bool ShouldRetryOn(Exception exception, IReadOnlySet<int>? additionalErrorNumbers)
        => false;

    /// <summary>
    /// Creates the exception that <see cref="SetParameterType" /> throws for a parameter of another driver.
    /// </summary>
    /// <param name="parameter">The rejected parameter.</param>
    /// <returns>The exception to throw.</returns>
    protected InvalidOperationException CreateForeignParameterException(DbParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return DamengDriverErrors.ForeignParameter(this, parameter);
    }

    /// <inheritdoc />
    public bool Equals(DamengDriver? other)
        => other is not null && other.GetType() == GetType();

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => Equals(obj as DamengDriver);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Name);

    /// <inheritdoc />
    public override string ToString()
        => Name;
}
