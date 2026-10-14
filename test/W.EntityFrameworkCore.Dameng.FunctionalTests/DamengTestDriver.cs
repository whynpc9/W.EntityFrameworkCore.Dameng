using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// Selects the driver under test. Shared tests call <c>UseDamengUnderTest</c> instead of a
/// driver-specific <c>Use...</c> method; tests that must assert different behavior per driver
/// branch on <see cref="DamengTestDriver.IsWDriver" /> or <c>#if WDRIVER</c>.
/// </summary>
internal static class DamengTestDriver
{
#if WDRIVER
    public const string Name = "W";

    public const string ProviderName = "W.EntityFrameworkCore.Dameng.WDriver";
#else
    public const string Name = "Official";

    public const string ProviderName = "W.EntityFrameworkCore.Dameng";
#endif

    public static bool IsWDriver
        => Name == "W";

    public static DbContextOptionsBuilder UseDamengUnderTest(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
#if WDRIVER
        => TolerateManyServiceProviders(optionsBuilder).UseDamengWithWDriver(connectionString, damengOptionsAction);
#else
        => TolerateManyServiceProviders(optionsBuilder).UseDameng(connectionString, damengOptionsAction);
#endif

    public static DbContextOptionsBuilder UseDamengUnderTest(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        => optionsBuilder.UseDamengUnderTest(connection, contextOwnsConnection: false, damengOptionsAction);

    public static DbContextOptionsBuilder UseDamengUnderTest(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
#if WDRIVER
        => TolerateManyServiceProviders(optionsBuilder).UseDamengWithWDriver(connection, contextOwnsConnection, damengOptionsAction);
#else
        => TolerateManyServiceProviders(optionsBuilder).UseDameng(connection, contextOwnsConnection, damengOptionsAction);
#endif

    public static DbContextOptionsBuilder<TContext> UseDamengUnderTest<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)optionsBuilder)
            .UseDamengUnderTest(connectionString, damengOptionsAction);

    public static DbContextOptionsBuilder<TContext> UseDamengUnderTest<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)optionsBuilder)
            .UseDamengUnderTest(connection, damengOptionsAction);

    public static DbContextOptionsBuilder<TContext> UseDamengUnderTest<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DamengDbContextOptionsBuilder>? damengOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)optionsBuilder)
            .UseDamengUnderTest(connection, contextOwnsConnection, damengOptionsAction);

    // The suites deliberately build many model/service variants in one process; EF's
    // more-than-twenty-providers guard is meant for applications, so tests only log it.
    private static DbContextOptionsBuilder TolerateManyServiceProviders(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.ConfigureWarnings(
            warnings => warnings.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));

    /// <summary>
    /// Derives the driver's connection string from the shared test connection without printing it.
    /// W uses its own typed builder; the non-secret transport mode comes from the launcher.
    /// </summary>
    public static string? DeriveConnectionString(string? configured)
    {
#if WDRIVER
        if (string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var source = new DbConnectionStringBuilder { ConnectionString = configured };
        var builder = new DmConnectionStringBuilder();
        foreach (string key in source.Keys)
        {
            builder[key] = source[key];
        }

        var transportSecurity = Environment.GetEnvironmentVariable(TransportSecurityVariable);
        if (!string.IsNullOrWhiteSpace(transportSecurity))
        {
            builder.Remove("transport_security");
            builder["transport_security"] = transportSecurity;
        }

        return builder.ConnectionString;
#else
        return configured;
#endif
    }

    public const string TransportSecurityVariable = "DAMENG_TEST_W_TRANSPORT_SECURITY";
}
