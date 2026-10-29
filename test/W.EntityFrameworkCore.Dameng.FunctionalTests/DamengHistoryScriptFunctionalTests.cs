using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Plan", "016")]
public sealed class DamengHistoryScriptFunctionalTests
{
    [DamengTheory]
    [InlineData("ordinary")][InlineData("keyword")][InlineData("quoted")]
    public async Task HistoryCreationWithKeywordLikeSchemaIsRepeatable(string kind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var schema = kind switch { "keyword" => "CREATE TABLE " + suffix, "quoted" => "模式\"'." + suffix, _ => "EF16_S_" + suffix };
        var table = "H\"点_" + suffix;
        await WithOwnedHistory(schema, table, async (connectionString, connection) =>
        {
            await using var context = Context(connectionString, table, schema);
            var history = context.GetService<IHistoryRepository>();
            var script = history.GetCreateIfNotExistsScript();
            await DamengScriptExecutor.ExecuteAsync(connection, script, true, text => text);
            await DamengScriptExecutor.ExecuteAsync(connection, script, true, text => text);
            Assert.Equal(1L, await CountSchema(connection, schema));
            Assert.Equal(1L, await CountTable(connection, schema, table));
            await Execute(connection, history.GetInsertScript(new HistoryRow("202610280016_single", "10.0.12")));
            await DamengScriptExecutor.ExecuteAsync(connection, script, true, text => text);
            Assert.Equal("1", await Scalar(connection, "SELECT COUNT(*) FROM " + Quote(schema) + "." + Quote(table)));
            Assert.Equal("202610280016_single", await Scalar(connection, "SELECT \"MigrationId\" FROM " + Quote(schema) + "." + Quote(table)));
        });
    }

    [DamengFact]
    public async Task IdempotentHistoryGuardKeepsMigrationRowSingle()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var schema = "CREATE TABLE " + suffix; var table = "EF16_H_" + suffix;
        await WithOwnedHistory(schema, table, async (connectionString, connection) =>
        {
            await using var context = new MigrationContext(new DbContextOptionsBuilder<MigrationContext>()
                .UseDamengUnderTest(connectionString, d => d.MigrationsHistoryTable(table, schema)).Options);
            var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains("202610280016_HistoryOnly", script);
            await DamengScriptExecutor.ExecuteAsync(connection, script, true, text => text);
            await DamengScriptExecutor.ExecuteAsync(connection, script, true, text => text);
            var applied = await context.GetService<IHistoryRepository>().GetAppliedMigrationsAsync();
            Assert.Equal("202610280016_HistoryOnly", Assert.Single(applied).MigrationId);
            Assert.Equal("1", await Scalar(connection, "SELECT COUNT(*) FROM " + Quote(schema) + "." + Quote(table)));
        });
    }

    [DamengTheory]
    [InlineData(false)][InlineData(true)]
    public async Task CreateIfNotExistsRunsDdlWithoutTransaction(bool async)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var schema = "EF16_API_" + suffix; var table = "EF16_H_" + suffix;
        await WithOwnedHistory(schema, table, async (connectionString, connection) =>
        {
            var commands = new Commands(); var transactions = new Transactions();
            await using var context = new DbContext(new DbContextOptionsBuilder().UseDamengUnderTest(connectionString,
                d => d.MigrationsHistoryTable(table, schema)).AddInterceptors(commands, transactions).Options);
            var history = context.GetService<IHistoryRepository>();
            Assert.True(async ? await history.CreateIfNotExistsAsync() : history.CreateIfNotExists());
            Assert.False(async ? await history.CreateIfNotExistsAsync() : history.CreateIfNotExists());
            Assert.True(commands.DdlCount > 0);
            Assert.False(commands.SawTransaction);
            Assert.Equal(0, transactions.Starts);
            Assert.Equal(1L, await CountTable(connection, schema, table));
        });
    }

    private static DbContext Context(string connectionString, string table, string schema)
        => new(new DbContextOptionsBuilder().UseDamengUnderTest(connectionString, d => d.MigrationsHistoryTable(table, schema)).Options);

    private static async Task WithOwnedHistory(string schema, string table, Func<string, DmConnection, Task> body)
    {
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        await using var connection = new DmConnection(connectionString); await connection.OpenAsync();
        // Record exact attempted names before any CREATE, regardless of its response.
        Assert.Equal(0L, await CountSchema(connection, schema));
        Assert.Equal(0L, await CountTable(connection, schema, table));
        try { await body(connectionString, connection); }
        finally
        {
            await using var cleanup = new DmConnection(connectionString); await cleanup.OpenAsync();
            if (await CountTable(cleanup, schema, table) != 0)
                await Execute(cleanup, "DROP TABLE " + Quote(schema) + "." + Quote(table));
            Assert.Equal(0L, await CountTable(cleanup, schema, table));
            if (await CountSchema(cleanup, schema) != 0) await Execute(cleanup, "DROP SCHEMA " + Quote(schema));
            Assert.Equal(0L, await CountSchema(cleanup, schema));
        }
    }

    private static async Task<long> CountSchema(DbConnection connection, string schema)
        => long.Parse(await Scalar(connection, "SELECT COUNT(*) FROM SYS.SYSOBJECTS WHERE TYPE$='SCH' AND NAME=:schema", ("schema", schema)), CultureInfo.InvariantCulture);
    private static async Task<long> CountTable(DbConnection connection, string schema, string table)
        => long.Parse(await Scalar(connection, "SELECT COUNT(*) FROM SYS.SYSOBJECTS O JOIN SYS.SYSOBJECTS S ON O.SCHID=S.ID "
            + "WHERE S.TYPE$='SCH' AND S.NAME=:schema AND O.SUBTYPE$='UTAB' AND O.NAME=:name", ("schema", schema), ("name", table)), CultureInfo.InvariantCulture);
    private static async Task<string> Scalar(DbConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 15;
        foreach (var (name, value) in parameters) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
    }
    private static async Task Execute(DbConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 15; await command.ExecuteNonQueryAsync(); }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed class Commands : DbCommandInterceptor
    {
        public int DdlCount { get; private set; }
        public bool SawTransaction { get; private set; }
        private void Observe(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                || command.CommandText.TrimStart().StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            { DdlCount++; SawTransaction |= command.Transaction is not null; }
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Observe(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
    }
    private sealed class Transactions : DbTransactionInterceptor
    {
        public int Starts { get; private set; }
        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result) { Starts++; return result; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        { Starts++; return ValueTask.FromResult(result); }
    }
    public sealed class MigrationContext(DbContextOptions<MigrationContext> options) : DbContext(options);
    [DbContext(typeof(MigrationContext))]
    [Migration("202610280016_HistoryOnly")]
    public sealed class HistoryOnlyMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) { }
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
