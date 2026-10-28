#pragma warning disable EF1001
#pragma warning disable EF1002 // Only generated object names enter SQL.
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
public sealed class DamengDatabaseCreatorFunctionalTests(ITestOutputHelper output)
{
    private static readonly bool[] FailureCases = [false, true];
    [DamengFact]
    public Task HasTablesSyncAndAsyncSeeAnExistingUnrelatedTable()
        => WithUnrelatedTable(async () =>
        {
            var target = UniqueName("EF10_HMD_");
            await using var context = Context(target: target);
            var creator = context.GetService<IRelationalDatabaseCreator>();
            Assert.True(creator.HasTables()); Assert.True(await creator.HasTablesAsync());
            Assert.False(context.Database.EnsureCreated());
            Assert.False(await context.Database.EnsureCreatedAsync());
            Assert.Equal(0, await CountTable(target));
            output.WriteLine($"{DamengTestDriver.Name}: unrelated table detected; EnsureCreated/Async false; model table not created.");
        });

    [DamengFact]
    public Task HasTablesFlowsCommandTimeoutAndScalarDiagnostics()
        => WithUnrelatedTable(async () =>
        {
            var probe = new ScalarProbe();
            await using var context = Context(probe: probe);
            var creator = context.GetService<IRelationalDatabaseCreator>();
            Assert.True(creator.HasTables()); Assert.True(await creator.HasTablesAsync());
            Assert.Equal(1, probe.SyncExecuting); Assert.Equal(1, probe.AsyncExecuting);
            Assert.Equal(1, probe.SyncExecuted); Assert.Equal(1, probe.AsyncExecuted);
            Assert.All(probe.Timeouts, timeout => Assert.Equal(17, timeout));
            Assert.All(probe.Sources, source => Assert.Equal(CommandSource.Migrations, source));
            Assert.All(probe.Contexts, observed => Assert.Same(context, observed));
            probe.Throw = true;
            Assert.Throws<InvalidOperationException>(() => creator.HasTables());
            await Assert.ThrowsAsync<InvalidOperationException>(() => creator.HasTablesAsync());
            Assert.Equal(2, probe.Failed);
            output.WriteLine($"{DamengTestDriver.Name}: actual scalar timeout=17, sync/async interceptors, success/failure diagnostics and Migrations source verified.");
        });

    [DamengFact]
    public Task HasTablesPreservesCallerOwnedConnectionAndTransaction()
        => WithUnrelatedTable(async () =>
        {
            await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
            await connection.OpenAsync();
            var probe = new ScalarProbe();
            await using (var context = Context(probe: probe, connection: connection))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var expected = transaction.GetDbTransaction();
                var creator = context.GetService<IRelationalDatabaseCreator>();
                Assert.True(creator.HasTables()); Assert.True(await creator.HasTablesAsync());
                Assert.All(probe.Transactions, observed => Assert.Same(expected, observed));
                Assert.All(probe.Connections, observed => Assert.Same(connection, observed));
                probe.Throw = true;
                Assert.Throws<InvalidOperationException>(() => creator.HasTables());
                await Assert.ThrowsAsync<InvalidOperationException>(() => creator.HasTablesAsync());
                Assert.Equal(ConnectionState.Open, connection.State);
                probe.Throw = false;
                Assert.Equal(1, (await context.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM DUAL").ToListAsync()).Single());
                Assert.Same(expected, context.Database.CurrentTransaction!.GetDbTransaction());
                await transaction.RollbackAsync();
            }
            Assert.Equal(ConnectionState.Open, connection.State);
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT 1 FROM DUAL";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            output.WriteLine($"{DamengTestDriver.Name}: caller-owned connection remains Open/usable after success, synthetic failure, transaction rollback and context disposal.");
        });

    [DamengFact]
    public Task HasTablesClosesOnlyConnectionsItOpened()
        => WithUnrelatedTable(async () =>
        {
            var probe = new ScalarProbe();
            await using var context = Context(probe: probe);
            var connection = context.Database.GetDbConnection();
            var creator = context.GetService<IRelationalDatabaseCreator>();
            foreach (var fail in FailureCases)
            {
                probe.Throw = fail;
                if (fail)
                {
                    Assert.Throws<InvalidOperationException>(() => creator.HasTables());
                    await Assert.ThrowsAsync<InvalidOperationException>(() => creator.HasTablesAsync());
                }
                else { Assert.True(creator.HasTables()); Assert.True(await creator.HasTablesAsync()); }
                Assert.Equal(ConnectionState.Closed, connection.State);
            }
            probe.Throw = false;
            await context.Database.OpenConnectionAsync();
            Assert.True(creator.HasTables()); Assert.True(await creator.HasTablesAsync());
            Assert.Equal(ConnectionState.Open, connection.State);
            await context.Database.CloseConnectionAsync();
            using var cancel = new CancellationTokenSource(); await cancel.CancelAsync();
            var executions = probe.SyncExecuting + probe.AsyncExecuting;
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creator.HasTablesAsync(cancel.Token));
            Assert.Equal(cancel.Token, error.CancellationToken);
            Assert.Equal(executions, probe.SyncExecuting + probe.AsyncExecuting);
            Assert.Equal(ConnectionState.Closed, connection.State);
            output.WriteLine($"{DamengTestDriver.Name}: internal Open/Close balanced on success/synthetic failure; held connection preserved; pre-cancel zero commands and Closed.");
        });

    private static CreatorContext Context(ScalarProbe? probe = null, DbConnection? connection = null, string target = "EF10_011_UNUSED")
    {
        var builder = new DbContextOptionsBuilder<CreatorContext>().ReplaceService<IModelCacheKeyFactory, KeyFactory>();
        if (connection is null) builder.UseDamengUnderTest(DamengTestEnvironment.GetRequiredConnectionString(), d => d.CommandTimeout(17));
        else builder.UseDamengUnderTest(connection, contextOwnsConnection: false, d => d.CommandTimeout(17));
        if (probe is not null) builder.AddInterceptors(probe);
        return new CreatorContext(builder.Options, target);
    }
    private static string UniqueName(string prefix) => prefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
    private async Task WithUnrelatedTable(Func<Task> action)
    {
        var table = UniqueName("EF10_HTB_");
        try { await Execute($"CREATE TABLE \"{table}\" (\"ID\" INT)"); await action(); }
        finally
        {
            if (await CountTable(table) > 0) await Execute($"DROP TABLE \"{table}\" PURGE");
            Assert.Equal(0, await CountTable(table));
            output.WriteLine($"{DamengTestDriver.Name}: exact unrelated-table cleanup verified.");
        }
    }
    private static async Task<int> CountTable(string table)
    {
        await using var context = new DbContext(new DbContextOptionsBuilder().UseDamengUnderTest(DamengTestEnvironment.GetRequiredConnectionString()).Options);
        return await context.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = '{table}'").SingleAsync();
    }
    private static async Task Execute(string sql)
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString()); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    private sealed class CreatorContext(DbContextOptions<CreatorContext> options, string target) : DbContext(options)
    {
        public string Target { get; } = target;
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<Row>().ToTable(Target);
    }
    private sealed class Row { public int Id { get; set; } }
    private sealed class KeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((CreatorContext)context).Target, designTime);
    }
    private sealed class ScalarProbe : DbCommandInterceptor
    {
        public bool Throw { get; set; }
        public int SyncExecuting { get; private set; }
        public int AsyncExecuting { get; private set; }
        public int SyncExecuted { get; private set; }
        public int AsyncExecuted { get; private set; }
        public int Failed { get; private set; }
        public List<int> Timeouts { get; } = [];
        public List<CommandSource> Sources { get; } = [];
        public List<DbContext?> Contexts { get; } = [];
        public List<DbTransaction?> Transactions { get; } = [];
        public List<DbConnection?> Connections { get; } = [];
        private void Observe(DbCommand command, CommandEventData data, bool async)
        {
            Assert.Equal("SELECT COUNT(*) FROM USER_TABLES", command.CommandText);
            Assert.Equal(async, data.IsAsync);
            Assert.Equal(DbCommandMethod.ExecuteScalar, data.ExecuteMethod);
            Timeouts.Add(command.CommandTimeout); Sources.Add(data.CommandSource); Contexts.Add(data.Context);
            Transactions.Add(command.Transaction); Connections.Add(command.Connection);
            if (Throw) throw new InvalidOperationException("011 synthetic scalar failure");
        }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { SyncExecuting++; Observe(command, eventData, false); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { AsyncExecuting++; Observe(command, eventData, true); return ValueTask.FromResult(result); }
        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        { SyncExecuted++; Assert.False(eventData.IsAsync); return result; }
        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        { AsyncExecuted++; Assert.True(eventData.IsAsync); return ValueTask.FromResult(result); }
        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) { Failed++; Assert.False(eventData.IsAsync); }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        { Failed++; Assert.True(eventData.IsAsync); return Task.CompletedTask; }
    }
}
