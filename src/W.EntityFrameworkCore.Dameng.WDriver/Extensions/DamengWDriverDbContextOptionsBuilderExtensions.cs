using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.WDriver.Infrastructure.Internal;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring Entity Framework Core to use Dameng Database with the
/// <c>W.DamengProvider</c> driver.
/// </summary>
/// <remarks>
/// <para>
/// W connection strings use W's own keys. W requires TLS by default; a plaintext server needs an
/// explicit <c>transport_security=PlaintextAllowed</c>.
/// </para>
/// <para>
/// <see cref="DamengDbContextOptionsBuilder.EnableRetryOnFailure()" /> is not supported with this
/// driver and throws <see cref="NotSupportedException" />.
/// </para>
/// </remarks>
public static class DamengWDriverDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures a context to connect to Dameng Database through <c>W.DamengProvider</c> using a connection string.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connectionString">The W.DamengProvider connection string.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder UseDamengWithWDriver(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => DamengDriverConfiguration.UseDriver(
            optionsBuilder,
            DamengWDriver.Instance,
            connectionString,
            damengOptionsAction);

    /// <summary>
    /// Configures a context to connect to Dameng Database through <c>W.DamengProvider</c> using an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>W.Dm.DmConnection</c>.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder UseDamengWithWDriver(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => UseDamengWithWDriver(
            optionsBuilder,
            connection,
            contextOwnsConnection: false,
            damengOptionsAction);

    /// <summary>
    /// Configures a context to connect to Dameng Database through <c>W.DamengProvider</c> using an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>W.Dm.DmConnection</c>.</param>
    /// <param name="contextOwnsConnection">Whether the context disposes the connection.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder UseDamengWithWDriver(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => DamengDriverConfiguration.UseDriver(
            optionsBuilder,
            DamengWDriver.Instance,
            connection,
            contextOwnsConnection,
            damengOptionsAction);

    /// <summary>
    /// Configures a typed context to connect to Dameng Database through <c>W.DamengProvider</c> using a connection string.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connectionString">The W.DamengProvider connection string.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDamengWithWDriver<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDamengWithWDriver(
            (DbContextOptionsBuilder)optionsBuilder,
            connectionString,
            damengOptionsAction);

    /// <summary>
    /// Configures a typed context to connect to Dameng Database through <c>W.DamengProvider</c> using an existing connection.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>W.Dm.DmConnection</c>.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDamengWithWDriver<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDamengWithWDriver(
            (DbContextOptionsBuilder)optionsBuilder,
            connection,
            damengOptionsAction);

    /// <summary>
    /// Configures a typed context to connect to Dameng Database through <c>W.DamengProvider</c> using an existing connection.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>W.Dm.DmConnection</c>.</param>
    /// <param name="contextOwnsConnection">Whether the context disposes the connection.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDamengWithWDriver<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDamengWithWDriver(
            (DbContextOptionsBuilder)optionsBuilder,
            connection,
            contextOwnsConnection,
            damengOptionsAction);
}
