#pragma warning disable EF1001
#pragma warning disable EF1002 // Only generated test function names enter SQL.
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("AuditPlan", "011")]
public sealed class WDriverDatabaseCreatorAsyncFunctionalTests(ITestOutputHelper output)
{
    [DamengFact]
    public Task HasTablesAsyncReturnsWhileExecuteResponseIsPending() => Run("result");
    [DamengFact]
    public Task HasTablesAsyncCancellationAfterExecuteSendPreservesTheToken() => Run("cancel");
    [DamengFact]
    public Task HasTablesAsyncHonorsConfiguredCommandTimeout() => Run("timeout");

    private async Task Run(string scenario)
    {
        var function = "EF10_HSL_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        try
        {
            await Execute($"""
                CREATE FUNCTION "{function}"("SECONDS" INT) RETURN INT AS
                BEGIN
                    DBMS_LOCK.SLEEP("SECONDS");
                    RETURN "SECONDS";
                END;
                """);
            var connectionString = new DmConnectionStringBuilder(DamengTestEnvironment.GetRequiredConnectionString()) { Pooling = false }.ConnectionString;
            await using var relay = new WDriverRequestBarrierRelay(connectionString, resetOnAccept: false, observeFrames: true);
            var probe = new SlowHasTablesProbe(function, scenario == "timeout" ? 5 : 3);
            var timeout = scenario == "timeout" ? 1 : 15;
            await using var context = new DbContext(new DbContextOptionsBuilder()
                .UseDamengUnderTest(relay.ConnectionString, d => d.CommandTimeout(timeout)).AddInterceptors(probe).Options);
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await context.Database.OpenConnectionAsync(watchdog.Token);
            var connection = context.Database.GetDbConnection();
            var session = await Session(context);
            var observer = relay.Observer;
            var generation = await observer.ArmExecuteAsync(watchdog.Token);
            using var cancellation = new CancellationTokenSource();
            var pending = context.GetService<IRelationalDatabaseCreator>().HasTablesAsync(cancellation.Token);
            try
            {
                using var barrierDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await observer.WaitExecuteForwardedAsync(generation, barrierDeadline.Token);
                observer.AssertExecutePending(generation, pending);
                Assert.Equal(1, probe.Matches); Assert.Equal(0, probe.SyncCalls);
                Assert.Equal(timeout, probe.ActualTimeout); Assert.Equal(cancellation.Token, probe.Token);
                output.WriteLine($"{scenario}: exact HasTables scalar intercepted, complete ExecuteForwarded -> PendingResult; actual timeout={timeout}.");
                if (scenario == "result")
                {
                    Assert.True(await pending.WaitAsync(watchdog.Token));
                    Assert.Equal(ConnectionState.Open, connection.State);
                    Assert.Equal(session, await Session(context));
                    output.WriteLine("PendingResult -> real server result true; same held session.");
                }
                else
                {
                    if (scenario == "cancel")
                    {
                        output.WriteLine("PendingResult -> CancelRequested");
                        await cancellation.CancelAsync();
                        var error = await Assert.ThrowsAsync<W.Dm.DmOperationCanceledException>(() => pending.WaitAsync(watchdog.Token));
                        Assert.Equal(cancellation.Token, error.CancellationToken);
                        Assert.Equal(W.Dm.DmCancelSource.User, error.FailureInfo.CancelSource);
                        Assert.False(error.FailureInfo.ConnectionReusable);
                    }
                    else
                    {
                        var error = await Assert.ThrowsAsync<W.Dm.DmTimeoutException>(() => pending.WaitAsync(watchdog.Token));
                        Assert.Equal(W.Dm.DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
                        Assert.Equal(W.Dm.DmErrorKind.Timeout, error.FailureInfo.ErrorKind);
                        Assert.False(error.FailureInfo.ConnectionReusable);
                        output.WriteLine("PendingResult -> W TotalDeadline (not watchdog/Allocate/Prepare).");
                    }
                    Assert.Same(connection, context.Database.GetDbConnection());
                    Assert.True(connection.State is ConnectionState.Broken or ConnectionState.Closed);
                    output.WriteLine("Broken/Closed before recovery; FailureInfo nonreusable.");
                    await context.Database.OpenConnectionAsync(watchdog.Token);
                    Assert.NotEqual(session, await Session(context));
                    Assert.Equal(1, (await context.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM DUAL").ToListAsync(watchdog.Token)).Single());
                    output.WriteLine("Explicit EF recovery -> new session -> successful health query.");
                }
            }
            finally
            {
                if (!pending.IsCompleted)
                {
                    await cancellation.CancelAsync();
                    try { await pending.WaitAsync(watchdog.Token); }
                    catch (Exception error) when (error is OperationCanceledException or W.Dm.DmException) { }
                }
            }
        }
        finally
        {
            if (await CountFunction(function) > 0) await Execute($"DROP FUNCTION \"{function}\"");
            Assert.Equal(0, await CountFunction(function));
            output.WriteLine("Exact owned sleep-function cleanup verified.");
        }
    }
    private static async Task<long> Session(DbContext context)
        => (await context.Database.SqlQueryRaw<long>("SELECT SESSID() AS \"Value\" FROM DUAL").ToListAsync()).Single();
    private static async Task Execute(string sql)
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString()); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    private static async Task<int> CountFunction(string function)
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString()); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SYS.SYSOBJECTS WHERE NAME = :name AND SCHID = CURRENT_SCHID()";
        var name = command.CreateParameter(); name.ParameterName = "name"; name.Value = function; command.Parameters.Add(name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
    private sealed class SlowHasTablesProbe(string function, int seconds) : DbCommandInterceptor
    {
        public int Matches { get; private set; }
        public int SyncCalls { get; private set; }
        public int ActualTimeout { get; private set; }
        public CancellationToken Token { get; private set; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { SyncCalls++; throw new InvalidOperationException("HasTablesAsync unexpectedly used synchronous scalar interception."); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Assert.Equal("SELECT COUNT(*) FROM USER_TABLES", command.CommandText);
            Assert.True(eventData.IsAsync); Assert.Equal(CommandSource.Migrations, eventData.CommandSource);
            Assert.Equal(DbCommandMethod.ExecuteScalar, eventData.ExecuteMethod);
            Matches++; ActualTimeout = command.CommandTimeout; Token = cancellationToken;
            command.CommandText = $"SELECT \"{function}\"({seconds.ToString(CultureInfo.InvariantCulture)}) FROM DUAL";
            return ValueTask.FromResult(result);
        }
    }
}
