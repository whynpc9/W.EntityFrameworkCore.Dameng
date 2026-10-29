using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Plan", "015")]
public sealed class DamengCommentOnlyMigrationsFunctionalTests
{
    [DamengTheory]
    [InlineData(false)] [InlineData(true)]
    public Task IdentityCommentLifecyclePreservesRowsAndNextGeneratedKey(bool idempotent) => Lifecycle(true, idempotent);

    [DamengTheory]
    [InlineData(false)] [InlineData(true)]
    public Task OrdinaryColumnCommentLifecyclePreservesDefinition(bool idempotent) => Lifecycle(false, idempotent);

    private static async Task Lifecycle(bool identity, bool idempotent)
    {
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var table = "EF15_C_" + suffix; var historyName = "EF15_H_" + suffix;
        var comments = new string?[] { null, "中文 '引号'\n第二行", "更新注释", null };
        Context Create(string? comment) => new(new DbContextOptionsBuilder<Context>()
            .UseDamengUnderTest(connectionString, d => d.MigrationsHistoryTable(historyName))
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table, identity, comment);
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(0L, await CountObject(connection, table));
        Assert.Equal(0L, await CountObject(connection, historyName));
        try
        {
            await using var initial = Create(null);
            await DamengScriptExecutor.ExecuteAsync(connection, initial.Database.GenerateCreateScript(), false, text => text);
            initial.Add(new Row { Name = "baseline" }); await initial.SaveChangesAsync();
            Assert.Equal(17L, await initial.Set<Row>().Select(r => r.Id).SingleAsync());
            var originalDefinition = await Scalar(connection, "SELECT DBMS_METADATA.GET_DDL('TABLE', :name, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())) FROM DUAL", table);
            for (var stage = 0; stage < 3; stage++)
            {
                await using var source = Create(comments[stage]); await using var target = Create(comments[stage + 1]);
                var model = target.GetService<IDesignTimeModel>().Model;
                var operation = Assert.IsType<AlterColumnOperation>(Assert.Single(target.GetService<IMigrationsModelDiffer>().GetDifferences(
                    source.GetService<IDesignTimeModel>().Model.GetRelationalModel(), model.GetRelationalModel())));
                var commands = target.GetService<IMigrationsSqlGenerator>().Generate([operation], model,
                    idempotent ? MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Script);
                Assert.All(commands, c => Assert.True(c.TransactionSuppressed));
                var sql = string.Concat(commands.Select(c => c.CommandText));
                var history = target.GetService<IHistoryRepository>();
                var migrationId = "202610280015" + stage + "_" + suffix;
                var script = idempotent
                    ? history.GetCreateIfNotExistsScript() + "\n/\n" + history.GetBeginIfNotExistsScript(migrationId) + "\n"
                        + sql + history.GetInsertScript(new HistoryRow(migrationId, "10.0.12")) + history.GetEndIfScript()
                    : sql;
                await DamengScriptExecutor.ExecuteAsync(connection, script, idempotent, text => text);
                if (idempotent)
                {
                    await DamengScriptExecutor.ExecuteAsync(connection, script, true, text => text);
                    Assert.Equal("1", await Scalar(connection, "SELECT COUNT(*) FROM \"" + historyName + "\" WHERE \"MigrationId\"=:name", migrationId));
                }
                Assert.DoesNotContain("MODIFY", sql);
                Assert.DoesNotContain("DROP DEFAULT", sql);
                var column = identity ? "ID" : "NAME";
                Assert.Equal(comments[stage + 1] ?? "", await Scalar(connection,
                    "SELECT COMMENTS FROM USER_COL_COMMENTS WHERE TABLE_NAME=:name AND COLUMN_NAME='" + column + "'", table));
                Assert.Equal("baseline", await target.Set<Row>().Where(r => r.Id == 17).Select(r => r.Name).SingleAsync());
                var row = new Row { Name = "stage" + stage }; target.Add(row); await target.SaveChangesAsync();
                Assert.Equal(20L + stage * 3, row.Id);
                Assert.Equal(Enumerable.Range(0, stage + 2).Select(n => 17L + n * 3), await target.Set<Row>().OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
                var definition = await Scalar(connection, "SELECT DBMS_METADATA.GET_DDL('TABLE', :name, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())) FROM DUAL", table);
                Assert.Equal(DefinitionWithoutComments(originalDefinition), DefinitionWithoutComments(definition));
            }
            Assert.Equal("40", await Scalar(connection, "SELECT CHAR_LENGTH FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:name AND COLUMN_NAME='NAME'", table));
            Assert.Equal("N", await Scalar(connection, "SELECT NULLABLE FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:name AND COLUMN_NAME='NAME'", table));
            await Execute(connection, "INSERT INTO \"" + table + "\" DEFAULT VALUES");
            Assert.Equal("1", await Scalar(connection, "SELECT COUNT(*) FROM \"" + table + "\" WHERE \"NAME\"=:name", "seed"));
        }
        finally
        {
            await using var cleanup = new DmConnection(connectionString); await cleanup.OpenAsync();
            foreach (var name in new[] { table, historyName })
            {
                if (await CountObject(cleanup, name) != 0) await Execute(cleanup, "DROP TABLE \"" + name + "\"");
                Assert.Equal(0L, await CountObject(cleanup, name));
            }
        }
    }

    private static string DefinitionWithoutComments(string text)
        => string.Join('\n', DamengScriptExecutor.SplitStatements(text).Where(l => !l.TrimStart().StartsWith("COMMENT", StringComparison.OrdinalIgnoreCase)));
    private static async Task<long> CountObject(DbConnection connection, string table)
        => long.Parse(await Scalar(connection, "SELECT COUNT(*) FROM SYS.SYSOBJECTS WHERE SCHID=CURRENT_SCHID() AND SUBTYPE$='UTAB' AND NAME=:name", table), CultureInfo.InvariantCulture);
    private static async Task<string> Scalar(DbConnection connection, string sql, string value)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 15;
        var parameter = command.CreateParameter(); parameter.ParameterName = "name"; parameter.Value = value; command.Parameters.Add(parameter);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
    }
    private static async Task Execute(DbConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 15; await command.ExecuteNonQueryAsync(); }
    private sealed class Context(DbContextOptions<Context> options, string table, bool identity, string? comment) : DbContext(options)
    {
        public string Table => table; public bool Identity => identity; public string? Comment => comment;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var entity = model.Entity<Row>(); entity.ToTable(table); entity.HasKey(r => r.Id);
            var id = entity.Property(r => r.Id).HasColumnName("ID").UseDamengIdentityColumn(17, 3);
            var name = entity.Property(r => r.Name).HasColumnName("NAME").HasMaxLength(40).IsRequired().HasDefaultValue("seed");
            if (identity) id.HasComment(comment); else name.HasComment(comment);
        }
    }
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        { var c = (Context)context; return (c.Table, c.Identity, c.Comment, designTime); }
    }
    private sealed class Row { public long Id { get; set; } public string Name { get; set; } = ""; }
}
