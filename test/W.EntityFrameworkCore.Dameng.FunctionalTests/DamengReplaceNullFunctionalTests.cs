using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengReplaceNullFunctionalTests
{
    private static readonly bool[] LobModes = [false, true];
    private static readonly string[] Modes = ["constant", "parameter-null", "parameter-empty", "parameter-text", "column"];

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReplaceResultsAndPredicatesMatchClr(bool relationalNulls)
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = Create(store, commands, relationalNulls);
            string[] texts = ["abcabc", "aaa", "xyz", "", "中文'a中文"];
            var rows = texts.Select((s, i) => new Row { Id = i + 1, Name = s, LargeText = s,
                Replacement = i % 2 == 0 ? null : "雪'" }).ToArray();
            context.Rows.AddRange(rows);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            foreach (var lob in LobModes)
            foreach (var mode in Modes)
            {
                string? replacement = mode switch { "parameter-empty" => "", "parameter-text" => "雪'", _ => null };
                var values = lob ? context.Rows.Select(r => new Result { Id = r.Id, Value = r.LargeText })
                    : context.Rows.Select(r => new Result { Id = r.Id, Value = r.Name });
                var query = mode == "constant"
                    ? values.Select(r => new Result { Id = r.Id, Value = r.Value.Replace("a", null) })
                    : mode == "column"
                        ? lob ? context.Rows.Select(r => new Result { Id = r.Id, Value = r.LargeText.Replace("a", r.Replacement) })
                            : context.Rows.Select(r => new Result { Id = r.Id, Value = r.Name.Replace("a", r.Replacement) })
                        : values.Select(r => new Result { Id = r.Id, Value = r.Value.Replace("a", replacement) });
                string Expected(Row r) => r.Name.Replace("a", mode == "column" ? r.Replacement : replacement);
                commands.Commands.Clear();
                Assert.Equal(rows.Select(Expected), (await query.OrderBy(r => r.Id).ToListAsync()).Select(r => r.Value));
                Assert.Contains("REPLACE(", Assert.Single(commands.Commands), StringComparison.Ordinal);
                Assert.Empty(await query.Where(r => r.Value == null).ToListAsync());
                Assert.Equal(rows.Select(r => r.Id), await query.Where(r => r.Value != null).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());
                Assert.Equal(rows.Where(r => Expected(r) == "bcbc").Select(r => r.Id),
                    await query.Where(r => r.Value == "bcbc").OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());
                Assert.Equal(rows.Where(r => Expected(r) != "bcbc").Select(r => r.Id),
                    await query.Where(r => !(r.Value == "bcbc") && r.Id > 0).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());
            }
        });

    [DamengFact]
    public Task NclobReplacementPreservesLongResult()
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = Create(store, commands, false);
            var text = new string('长', 20_000) + "aa尾";
            context.Rows.Add(new Row { Id = 1, Name = "abc", LargeText = text });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();
            var value = await context.Rows.Select(r => r.LargeText.Replace("a", r.Replacement)).SingleAsync();
            Assert.Equal(text.Replace("a", null), value);
            Assert.Equal(20_001, value.Length);
            var sql = Assert.Single(commands.Commands);
            Assert.Contains("REPLACE(", sql, StringComparison.Ordinal);
            Assert.DoesNotContain(" AS VARCHAR", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" AS NVARCHAR", sql, StringComparison.OrdinalIgnoreCase);
        });

    private static Context Create(Store store, CommandCaptureInterceptor commands, bool relationalNulls)
        => new(new DbContextOptionsBuilder<Context>().UseDamengUnderTest(store.ConnectionString,
                o => { if (relationalNulls) o.UseRelationalNulls(); })
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(commands).Options, store.TableName);
    private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
    {
        public string Table { get; } = table;
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>(e => {
                e.ToTable(Table); e.Property(r => r.Id).ValueGeneratedNever();
                e.Property(r => r.Name).HasMaxLength(200).IsRequired();
                e.Property(r => r.LargeText).HasColumnType("NCLOB").IsRequired();
                e.Property(r => r.Replacement).HasMaxLength(200);
            });
    }
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Table, designTime);
    }
    private sealed class Result
    {
        public int Id { get; set; }
        public string Value { get; set; } = "";
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string LargeText { get; set; } = "";
        public string? Replacement { get; set; }
    }
    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Store
    {
        private bool _tableCreated;

        private Store()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_RN_{suffix}";
            PrimaryKeyName = $"PK_RN_{suffix}";
        }

        public string ConnectionString { get; }
        public string TableName { get; }
        public string PrimaryKeyName { get; }

        public static async Task WithTableAsync(Func<Store, Task> test)
        {
            var store = new Store();
            try
            {
                await store.CreateTableAsync();
                await test(store);
            }
            finally
            {
                await store.DropTableAsync();
            }
        }

        private async Task CreateTableAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE "{TableName}" (
                    "Id" INT NOT NULL,
                    "Name" NVARCHAR2(200) NOT NULL,
                    "LargeText" NCLOB NOT NULL,
                    "Replacement" NVARCHAR2(200) NULL,
                    CONSTRAINT "{PrimaryKeyName}" NOT CLUSTER PRIMARY KEY ("Id")
                )
                """;
            await command.ExecuteNonQueryAsync();
            _tableCreated = true;
        }

        private async Task DropTableAsync()
        {
            if (!_tableCreated)
            {
                return;
            }

            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE \"{TableName}\"";
            await command.ExecuteNonQueryAsync();
            _tableCreated = false;
            command.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "name";
            parameter.Value = TableName;
            command.Parameters.Add(parameter);
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
    }
}
