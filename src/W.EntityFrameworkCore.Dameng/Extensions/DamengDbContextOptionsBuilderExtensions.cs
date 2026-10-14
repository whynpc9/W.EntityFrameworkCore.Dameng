using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring Entity Framework Core to use Dameng Database with the official
/// <c>DM.DmProvider</c> driver.
/// </summary>
public static class DamengDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures a context to connect to Dameng Database using a connection string.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder UseDameng(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => DamengDriverConfiguration.UseDriver(
            optionsBuilder,
            DamengOfficialDriver.Instance,
            connectionString,
            damengOptionsAction);

    /// <summary>
    /// Configures a context to connect to Dameng Database using an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>Dm.DmConnection</c>.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder UseDameng(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => UseDameng(
            optionsBuilder,
            connection,
            contextOwnsConnection: false,
            damengOptionsAction);

    /// <summary>
    /// Configures a context to connect to Dameng Database using an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>Dm.DmConnection</c>.</param>
    /// <param name="contextOwnsConnection">Whether the context disposes the connection.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder UseDameng(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => DamengDriverConfiguration.UseDriver(
            optionsBuilder,
            DamengOfficialDriver.Instance,
            connection,
            contextOwnsConnection,
            damengOptionsAction);

    /// <summary>
    /// Configures a typed context to connect to Dameng Database using a connection string.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDameng<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDameng(
            (DbContextOptionsBuilder)optionsBuilder,
            connectionString,
            damengOptionsAction);

    /// <summary>
    /// Configures a typed context to connect to Dameng Database using an existing connection.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>Dm.DmConnection</c>.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDameng<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDameng(
            (DbContextOptionsBuilder)optionsBuilder,
            connection,
            damengOptionsAction);

    /// <summary>
    /// Configures a typed context to connect to Dameng Database using an existing connection.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="connection">The database connection; it must be a <c>Dm.DmConnection</c>.</param>
    /// <param name="contextOwnsConnection">Whether the context disposes the connection.</param>
    /// <param name="damengOptionsAction">Optional provider-specific configuration.</param>
    /// <returns>The same options builder so additional calls can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDameng<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDameng(
            (DbContextOptionsBuilder)optionsBuilder,
            connection,
            contextOwnsConnection,
            damengOptionsAction);
}
