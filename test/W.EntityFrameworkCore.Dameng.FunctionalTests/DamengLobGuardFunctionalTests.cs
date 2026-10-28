using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengLobGuardFunctionalTests
{
    private static readonly string[] BoundedValues = ["a", "b"];

    [DamengFact]
    public Task UnsupportedLobShapesFailBeforeCommandExecution()
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = await Create(store, commands);
            commands.Commands.Clear();
            for (var operation = 0; operation < 3; operation++)
            for (var side = 0; side < 2; side++)
            for (var binary = 0; binary < 2; binary++)
            {
                var error = binary == 0
                    ? await Record.ExceptionAsync(() => Set(Text(context, side == 0), Text(context, side == 1), operation).ToListAsync())
                    : await Record.ExceptionAsync(() => Set(Bytes(context, side == 0), Bytes(context, side == 1), operation).ToListAsync());
                Assert.NotNull(error);
                Assert.Contains(operation switch { 0 => "UNION", 1 => "INTERSECT", _ => "EXCEPT" }, error.ToString(), StringComparison.Ordinal);
                Assert.Contains(binary == 0 ? "NCLOB" : "BLOB", error.ToString(), StringComparison.Ordinal);
                Assert.Empty(commands.Commands);
            }
            for (var concat = 0; concat < 2; concat++)
            for (var descending = 0; descending < 2; descending++)
            for (var secondary = 0; secondary < 2; secondary++)
            {
                var error = await Record.ExceptionAsync(() => Aggregate(context, concat != 0, descending != 0, secondary != 0, true).ToListAsync());
                Assert.NotNull(error);
                Assert.Contains("order by a LOB", error.ToString(), StringComparison.Ordinal);
                Assert.Empty(commands.Commands);
            }
        });

    [DamengFact]
    public Task SupportedLobControlsExecuteOnServer()
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = await Create(store, commands);
            commands.Commands.Clear();
            for (var operation = 0; operation < 3; operation++)
            {
                var expected = operation == 2 ? Array.Empty<string>() : BoundedValues;
                Assert.Equal(expected, (await Set(Text(context, false), Text(context, false), operation).ToListAsync()).Order(StringComparer.Ordinal));
            }
            Assert.Equal(["a", "a", "b", "b"], (await Text(context, true).Concat(Text(context, true)).ToListAsync()).Order(StringComparer.Ordinal));
            Assert.Equal(["01", "01", "02", "02"], (await Bytes(context, true).Concat(Bytes(context, true)).ToListAsync())
                .Select(value => Convert.ToHexString(value)).Order(StringComparer.Ordinal));
            Assert.Equal("a|b", Assert.Single(await Aggregate(context, false, false, false, false).ToListAsync()));
            Assert.Equal("ab", Assert.Single(await Aggregate(context, true, false, false, false).ToListAsync()));
            Assert.Contains(commands.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
            Assert.Contains(commands.Commands, sql => sql.Contains("LISTAGG(", StringComparison.Ordinal));
        });

    private static async Task<Context> Create(Store store, CommandCaptureInterceptor commands)
    {
        var context = new Context(new DbContextOptionsBuilder<Context>().UseDamengUnderTest(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(commands).Options, store.TableName);
        context.Rows.AddRange(new Row { Id = 1, GroupId = 1, ShortText = "a", LongText = "a", SmallBinary = [1], LargeBinary = [1] },
            new Row { Id = 2, GroupId = 1, ShortText = "b", LongText = "b", SmallBinary = [2], LargeBinary = [2] });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return context;
    }
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Table, designTime);
    }
    private static IQueryable<T> Set<T>(IQueryable<T> left, IQueryable<T> right, int operation)
        => operation switch { 0 => left.Union(right), 1 => left.Intersect(right), _ => left.Except(right) };
    private static IQueryable<string> Text(Context context, bool lob)
        => lob ? context.Rows.Select(r => r.LongText) : context.Rows.Select(r => r.ShortText);
    private static IQueryable<byte[]> Bytes(Context context, bool lob)
        => lob ? context.Rows.Select(r => r.LargeBinary) : context.Rows.Select(r => r.SmallBinary);
    private static IQueryable<string> Aggregate(Context context, bool concat, bool descending, bool secondary, bool lob)
    {
        if (!lob)
            return concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.Id).Select(r => r.ShortText)))
                : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.Id).Select(r => r.ShortText)));
        if (secondary)
            return descending
                ? concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.GroupId).ThenByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
                    : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.GroupId).ThenByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
                : concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.GroupId).ThenBy(r => r.LargeBinary).Select(r => r.ShortText)))
                    : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.GroupId).ThenBy(r => r.LargeBinary).Select(r => r.ShortText)));
        return descending
            ? concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
                : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
            : concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.LargeBinary).Select(r => r.ShortText)))
                : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.LargeBinary).Select(r => r.ShortText)));
    }
    private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
    {
        public string Table { get; } = table;
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>(e => {
                e.ToTable(Table); e.Property(r => r.Id).ValueGeneratedNever();
                e.Property(r => r.ShortText).HasMaxLength(200);
                e.Property(r => r.LongText).HasColumnType("NCLOB");
                e.Property(r => r.SmallBinary).HasMaxLength(16);
                e.Property(r => r.LargeBinary).HasColumnType("BLOB");
            });
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public string ShortText { get; set; } = "";
        public string LongText { get; set; } = "";
        public byte[] SmallBinary { get; set; } = [];
        public byte[] LargeBinary { get; set; } = [];
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
            TableName = $"EF10_LG_{suffix}";
            PrimaryKeyName = $"PK_LG_{suffix}";
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
                    "GroupId" INT NOT NULL,
                    "ShortText" NVARCHAR2(200) NOT NULL,
                    "LongText" NCLOB NOT NULL,
                    "SmallBinary" VARBINARY(16) NOT NULL,
                    "LargeBinary" BLOB NOT NULL,
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
