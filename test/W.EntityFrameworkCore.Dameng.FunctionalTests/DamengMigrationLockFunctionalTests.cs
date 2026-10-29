using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Plan", "017")]
public sealed class DamengMigrationLockFunctionalTests(ITestOutputHelper output)
{
    private const int LockId = 20260723;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [DamengTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ContenderIsExcludedAcrossDdlAndReleasedAfterDispose(bool async)
    {
        var connectionString = ConnectionForLockTests();
        await using var owner = Context(connectionString, "Owner");
        await owner.Database.OpenConnectionAsync().WaitAsync(Deadline);
        await using var contenderContext = Context(connectionString, "Contender");
        await contenderContext.Database.OpenConnectionAsync().WaitAsync(Deadline);
        var contender = contenderContext.Database.GetDbConnection();
        await AssertFree(contender);
        var ownConnection = owner.Database.GetDbConnection();
        var ownSession = await Session(ownConnection);
        Assert.NotEqual(ownSession, await Session(contender));
        var table = "EF17_LOCK_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var schema = Convert.ToString(await Scalar(ownConnection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"), CultureInfo.InvariantCulture)!;
        Assert.Equal(0L, await CountTable(contender, table));
        var journal = new TableJournal(schema, table);
        output.WriteLine("Recovery journal: " + journal.Path);
        IMigrationsDatabaseLock? handle = null;
        try
        {
            handle = await Acquire(owner, async);
            Assert.Equal(ownSession, await Session(ownConnection));
            await AssertExcluded(contender);
            Assert.Null(owner.Database.CurrentTransaction);
            journal.Save("create_unknown");
            await Execute(ownConnection, "CREATE TABLE " + Quote(table) + " (\"ID\" INT)");
            journal.Save("present");
            Assert.Equal(ownSession, await Session(ownConnection));
            await AssertExcluded(contender);
            await Execute(ownConnection, "INSERT INTO " + Quote(table) + " VALUES(1)");
            await Release(handle, async); handle = null;
            await AssertFree(contender);
            var again = await Acquire(owner, async); await Release(again, async);
            Assert.Equal(1L, Convert.ToInt64(await Scalar(contender, "SELECT COUNT(*) FROM " + Quote(table)), CultureInfo.InvariantCulture));
            output.WriteLine("Same held session: contender excluded before/after DDL; dispose -> contender/product reacquire; exact row visible.");
        }
        finally
        {
            try { if (handle is not null) await Release(handle, async); }
            finally
            {
                await using var cleanup = new DmConnection(connectionString); await cleanup.OpenAsync().WaitAsync(Deadline);
                if (await CountTable(cleanup, table) != 0) await Execute(cleanup, "DROP TABLE " + Quote(schema) + "." + Quote(table));
                Assert.Equal(0L, await CountTable(cleanup, table)); journal.Save("cleaned");
                await AssertFree(cleanup);
            }
        }
    }

    [DamengTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ExceptionInsideLockScopeReleasesOwnedLock(bool async)
    {
        var connectionString = ConnectionForLockTests();
        await using var owner = Context(connectionString, "ExceptionOwner");
        await owner.Database.OpenConnectionAsync().WaitAsync(Deadline);
        await using var contender = new DmConnection(connectionString); await contender.OpenAsync().WaitAsync(Deadline);
        await AssertFree(contender);
        await Assert.ThrowsAsync<ScopeFailure>(async () =>
        {
            if (async)
            {
                await using var handle = await Acquire(owner, true);
                await AssertExcluded(contender); throw new ScopeFailure();
            }
            else
            {
                using var handle = await Acquire(owner, false);
                await AssertExcluded(contender); throw new ScopeFailure();
            }
        }).WaitAsync(Deadline);
        await AssertFree(contender);
        var again = await Acquire(owner, async); await Release(again, async);
        await AssertFree(contender);
        output.WriteLine("Healthy held-lock exclusion -> synthetic scope exception preserved -> competing and product acquisition succeed.");
    }

    [DamengTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ClosingOwningConnectionReleasesLock(bool async)
    {
        var connectionString = ConnectionForLockTests();
        await using var owner = Context(connectionString, "CloseOwner"); await owner.Database.OpenConnectionAsync().WaitAsync(Deadline);
        await using var contender = new DmConnection(connectionString); await contender.OpenAsync().WaitAsync(Deadline);
        await using var witness = new DmConnection(connectionString); await witness.OpenAsync().WaitAsync(Deadline);
        await AssertFree(contender);
        IMigrationsDatabaseLock? handle = await Acquire(owner, async);
        var contenderOwns = false;
        try
        {
            await AssertExcluded(contender);
            if (async) await owner.Database.CloseConnectionAsync().WaitAsync(Deadline); else owner.Database.CloseConnection();
            Assert.Equal(ConnectionState.Closed, owner.Database.GetDbConnection().State);
            var result = await Request(contender); contenderOwns = result == 0; Assert.Equal(0, result);
            await AssertExcluded(witness);
            // Disposing the old connection's handle must not release the new owner's lock.
            await Release(handle, async); handle = null;
            await AssertExcluded(witness);
            await ReleaseProbe(contender); contenderOwns = false;
            await AssertFree(witness);
            await owner.Database.OpenConnectionAsync().WaitAsync(Deadline);
            var again = await Acquire(owner, async); await Release(again, async);
            await AssertFree(witness);
            output.WriteLine("Actual owner close -> contender owns -> old handle dispose leaves contender exclusive -> exact owner release/reacquire.");
        }
        finally
        {
            try { if (handle is not null) await Release(handle, async); }
            finally { if (contenderOwns) await ReleaseProbe(contender); }
        }
    }

    [DamengFact]
    public async Task PreCanceledAcquireDoesNotLeaveLockOwned()
    {
        var connectionString = ConnectionForLockTests();
        var commands = new LockCommands();
#if WDRIVER
        await using var relay = new WDriverRequestBarrierRelay(connectionString, resetOnAccept: false, observeFrames: true);
        var actualConnectionString = relay.ConnectionString;
#else
        var actualConnectionString = connectionString;
#endif
        await using var owner = Context(actualConnectionString, "PreCanceled", interceptors: [commands]);
        await owner.Database.OpenConnectionAsync().WaitAsync(Deadline);
        var connection = owner.Database.GetDbConnection();
        var originalSession = await Session(connection);
        await using var contender = new DmConnection(connectionString); await contender.OpenAsync().WaitAsync(Deadline);
        await AssertFree(contender);
#if WDRIVER
        using var barrierDeadline = new CancellationTokenSource(Deadline);
        var observer = relay.Observer;
        _ = await observer.ArmExecuteAsync(barrierDeadline.Token);
        var watermark = observer.Stages.Length;
        int? beforeCleanupExecuteCount = null;
        // Acquire's catch deliberately sends RELEASE. Snapshot before that separate
        // command, so its Execute is never misreported as a canceled REQUEST send.
        commands.BeforeRelease = () => beforeCleanupExecuteCount = observer.Stages.Skip(watermark).Count(IsExecute);
#endif
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.GetService<IHistoryRepository>()
            .AcquireDatabaseLockAsync(cancellation.Token).WaitAsync(Deadline));
        await AssertFree(contender);
        Assert.Equal(originalSession, await Session(connection));
        Assert.Equal(1, Convert.ToInt32(await Scalar(connection, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture));
#if WDRIVER
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(0, beforeCleanupExecuteCount);
        output.WriteLine("Warm idle watermark -> pre-canceled REQUEST has zero complete Execute forwards before cleanup RELEASE (observed separately).");
#else
        Assert.True(cancellation.IsCancellationRequested);
        output.WriteLine("Official pre-canceled acquire: " + failure.GetType().Name
            + "; original-token identity=" + (failure.CancellationToken == cancellation.Token)
            + "; reported-token canceled=" + failure.CancellationToken.IsCancellationRequested
            + ". Held session healthy, competing lock free; token identity/network-zero-send are not official-driver guarantees.");
#endif
    }

#if WDRIVER
    [DamengFact]
    public async Task CancelWaitingAcquirePreservesTokenAndAllowsLaterAcquisition()
    {
        var connectionString = ConnectionForLockTests();
        await using var owner = Context(connectionString, "WaitOwner", timeout: 20);
        await owner.Database.OpenConnectionAsync().WaitAsync(Deadline);
        await using var monitor = new DmConnection(connectionString); await monitor.OpenAsync().WaitAsync(Deadline);
        await AssertFree(monitor);
        // Validate ordinary-profile catalog visibility before taking any lock.
        _ = await Scalar(monitor, "SELECT COUNT(*) FROM V$DBMS_LOCKS WHERE \"ID\"=" + LockId
            + " AND \"OWN_SESS\"=0 AND \"WAIT_SESS\"=0 AND \"MODE\"='X'");
        await using var relay = new WDriverRequestBarrierRelay(connectionString, resetOnAccept: false, observeFrames: true);
        await using var waiting = Context(relay.ConnectionString, "WaitContender", timeout: 20);
        await waiting.Database.OpenConnectionAsync().WaitAsync(Deadline);
        var ownerSession = await Session(owner.Database.GetDbConnection());
        var waiterSession = await Session(waiting.Database.GetDbConnection());
        Assert.NotEqual(ownerSession, waiterSession);
        var observer = relay.Observer;
        using var barrierDeadline = new CancellationTokenSource(Deadline);
        var generation = await observer.ArmExecuteAsync(barrierDeadline.Token);
        var watermark = observer.Stages.Length;
        using var cancellation = new CancellationTokenSource();
        IMigrationsDatabaseLock? ownerLock = null;
        Task<IMigrationsDatabaseLock>? pending = null;
        try
        {
            ownerLock = await Acquire(owner, true);
            await AssertExcluded(monitor);
            pending = waiting.GetService<IHistoryRepository>().AcquireDatabaseLockAsync(cancellation.Token);
            await observer.WaitExecuteForwardedAsync(generation, barrierDeadline.Token);
            observer.AssertExecutePending(generation, pending);
            Assert.Single(observer.Stages.Skip(watermark), IsExecute);
            await WaitForServerWait(monitor, waiterSession, ownerSession);
            observer.AssertExecutePending(generation, pending);
            output.WriteLine("Complete REQUEST Execute forwarded after idle watermark; ordinary V$DBMS_LOCKS matches fixed lock ID, waiter and owner sessions in X wait.");
            await cancellation.CancelAsync();
            var failure = await Assert.ThrowsAsync<W.Dm.DmOperationCanceledException>(() => pending.WaitAsync(Deadline));
            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Equal(W.Dm.DmCancelSource.User, failure.FailureInfo.CancelSource);
            Assert.False(failure.FailureInfo.ConnectionReusable);
            var state = waiting.Database.GetDbConnection().State;
            if (state == ConnectionState.Open)
                Assert.NotEqual(waiterSession, await Session(waiting.Database.GetDbConnection()));
            else Assert.True(state is ConnectionState.Closed or ConnectionState.Broken);
            await AssertExcluded(monitor);
            await ownerLock.DisposeAsync().AsTask().WaitAsync(Deadline); ownerLock = null;
            await using var healthy = Context(connectionString, "FreshAfterCancel"); await healthy.Database.OpenConnectionAsync().WaitAsync(Deadline);
            var again = await Acquire(healthy, true); await Release(again, true);
            await AssertFree(monitor);
            output.WriteLine("Waiting cancellation preserves original token/nonreusable FailureInfo; holder unaffected; fresh product session reacquires after release.");
        }
        finally
        {
            try
            {
                await cancellation.CancelAsync();
                // Even if a cancellation assertion fails, release only our own holder so
                // any surviving waiter can finish before its context/relay is disposed.
                if (ownerLock is not null) { await Release(ownerLock, true); ownerLock = null; }
                if (pending is not null)
                {
                    try { var acquired = await pending.WaitAsync(Deadline); await Release(acquired, true); }
                    catch (Exception error) when (error is OperationCanceledException or W.Dm.DmException) { }
                }
            }
            finally { await AssertFree(monitor); }
        }
    }
    private static bool IsExecute(WRequestStage stage) => stage is WRequestStage.DirectExecute or WRequestStage.PreparedExecute;
    private static async Task WaitForServerWait(DbConnection monitor, long waiter, long owner)
    {
        var start = System.Diagnostics.Stopwatch.StartNew();
        while (start.Elapsed < TimeSpan.FromSeconds(5))
        {
            var count = Convert.ToInt32(await Scalar(monitor, "SELECT COUNT(*) FROM V$DBMS_LOCKS WHERE \"ID\"=" + LockId
                + " AND \"OWN_SESS\"=:waiter AND \"WAIT_SESS\"=:owner AND \"MODE\"='X'", ("waiter", waiter), ("owner", owner)), CultureInfo.InvariantCulture);
            if (count == 1) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("BLOCKED: ordinary-profile server evidence did not confirm this exact lock waiter/holder pair.");
    }
#endif

    private sealed class ScopeFailure : Exception;
    private sealed class LockCommands : DbCommandInterceptor
    {
        public Action? BeforeRelease { get; set; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DBMS_LOCK.RELEASE", StringComparison.Ordinal)) BeforeRelease?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private static string ConnectionForLockTests()
    {
#if WDRIVER
        return new DmConnectionStringBuilder(DamengTestEnvironment.GetRequiredConnectionString()) { Pooling = false }.ConnectionString;
#else
        // The locked official builder has no Pooling property/keyword. Verify its actual
        // Close behavior with a competing physical session rather than inventing a key.
        return DamengTestEnvironment.GetRequiredConnectionString();
#endif
    }
    private static DbContext Context(string connectionString, string history, int timeout = 5, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder().UseDamengUnderTest(connectionString,
            d => { d.CommandTimeout(timeout); d.MigrationsHistoryTable("EF17_" + history, "Metadata_" + history); }).AddInterceptors(interceptors).Options);
    private static async Task<IMigrationsDatabaseLock> Acquire(DbContext context, bool async)
        => async ? await context.GetService<IHistoryRepository>().AcquireDatabaseLockAsync().WaitAsync(Deadline)
            : context.GetService<IHistoryRepository>().AcquireDatabaseLock();
    private static async Task Release(IMigrationsDatabaseLock handle, bool async)
    { if (async) await handle.DisposeAsync().AsTask().WaitAsync(Deadline); else handle.Dispose(); }
    private static async Task<int> Request(DbConnection connection)
        => Convert.ToInt32(await Scalar(connection, $"SELECT DBMS_LOCK.REQUEST({LockId},6,0) FROM DUAL"), CultureInfo.InvariantCulture);
    private static async Task ReleaseProbe(DbConnection connection)
        => Assert.Equal(0, Convert.ToInt32(await Scalar(connection, $"SELECT DBMS_LOCK.RELEASE({LockId}) FROM DUAL"), CultureInfo.InvariantCulture));
    private static async Task AssertExcluded(DbConnection connection)
    {
        var result = await Request(connection);
        if (result == 0) await ReleaseProbe(connection); // Never leak an unexpectedly acquired probe lock.
        Assert.Equal(1, result);
    }
    private static async Task AssertFree(DbConnection connection)
    {
        var result = await Request(connection);
        Assert.True(result == 0, "BLOCKED: fixed migration lock is occupied or REQUEST signature/return code differs: " + result);
        await ReleaseProbe(connection);
    }
    private static async Task<long> Session(DbConnection connection)
        => Convert.ToInt64(await Scalar(connection, "SELECT SESSID() FROM DUAL"), CultureInfo.InvariantCulture);
    private static async Task<long> CountTable(DbConnection connection, string table)
        => Convert.ToInt64(await Scalar(connection, "SELECT COUNT(*) FROM SYS.SYSOBJECTS WHERE SCHID=CURRENT_SCHID() AND SUBTYPE$='UTAB' AND NAME=:name", ("name", table)), CultureInfo.InvariantCulture);
    private static async Task<object?> Scalar(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 5;
        foreach (var (name, value) in parameters) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
        return await command.ExecuteScalarAsync().WaitAsync(Deadline);
    }
    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 5;
        Assert.Null(command.Transaction);
        await command.ExecuteNonQueryAsync().WaitAsync(Deadline);
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed class TableJournal
    {
        public string Path { get; }
        private readonly string _schema;
        private readonly string _table;
        public TableJournal(string schema, string table)
        {
            _schema = schema; _table = table;
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "W.EntityFrameworkCore.Dameng.slnx"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Repository recovery directory unavailable.");
            var directory = System.IO.Path.Combine(root.FullName, "artifacts", "query-translation", "local-test", "migration-lock-" + Guid.NewGuid().ToString("N"), "recovery");
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Path = System.IO.Path.Combine(directory, "table.json"); Save("planned_absent");
        }
        public void Save(string stage)
        {
            var temporary = Path + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            { JsonSerializer.Serialize(file, new { Schema = _schema, Table = _table, Stage = stage, Updated = DateTimeOffset.UtcNow }); file.Flush(true); }
            File.Move(temporary, Path, true);
        }
    }
}
