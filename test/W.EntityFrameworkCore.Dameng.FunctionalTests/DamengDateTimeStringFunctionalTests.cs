using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengDateTimeStringFunctionalTests
{
#pragma warning disable CA1305 // The no-culture overload intentionally follows server text formatting.
    [DamengFact]
    public async Task DefaultTextMatchesNativeCastInProjectionFilterOrderAndGroup()
    {
        await using var store = await DateStringStore.CreateAsync();
        var oracle = await store.NativeTextAsync();
        var context = store.Context;
        store.Commands.Clear();
        var actual = await context.Rows.OrderBy(r => r.Id).Select(r => new TextRow
        {
            Id = r.Id,
            Stamp = r.Stamp.ToString(),
            Date = r.DateValue.ToString(),
            Native = r.NativeAt.ToString(),
            Month = r.MonthValue.ToString(),
            Nullable = r.OptionalStamp!.Value.ToString(),
            NullableFallback = r.OptionalStamp.ToString()
        }).ToListAsync();
        Assert.Equal(oracle, actual);
        AssertExecuted(store, "VARCHAR(100)");
        Assert.Contains(".1234567", actual.Single(r => r.Id == 3).Stamp, StringComparison.Ordinal);
        Assert.Contains(".1234568", actual.Single(r => r.Id == 4).Stamp, StringComparison.Ordinal);
        Assert.Null(actual.Single(r => r.Id == 6).Nullable);
        Assert.Equal(string.Empty, actual.Single(r => r.Id == 6).NullableFallback);

        store.Commands.Clear();
        var key = oracle[2].Stamp;
        var matching = await context.Rows.Where(r => r.Stamp.ToString() == key).Select(r => r.Id).ToListAsync();
        Assert.Equal([3], matching);
        AssertExecuted(store, "VARCHAR(100)", "WHERE");

        store.Commands.Clear();
        var ordered = await context.Rows.OrderBy(r => r.Stamp.ToString()).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
        Assert.Equal(oracle.OrderBy(r => r.Stamp, StringComparer.Ordinal).ThenBy(r => r.Id).Select(r => r.Id), ordered);
        AssertExecuted(store, "VARCHAR(100)", "ORDER BY");

        store.Commands.Clear();
        var fullGroups = await context.Rows.GroupBy(r => r.Stamp.ToString())
            .Select(g => new TextCount { Key = g.Key, Count = g.Count() }).ToListAsync();
        AssertCounts(oracle.Select(r => r.Stamp), fullGroups);
        Assert.Equal(7, fullGroups.Count);
        var sql = AssertExecuted(store, "VARCHAR(100)", "GROUP BY");
        Assert.DoesNotContain("TRUNC(", sql, StringComparison.Ordinal);

        store.Commands.Clear();
        var monthGroups = await context.Rows.GroupBy(r => r.MonthValue.ToString())
            .Select(g => new TextCount { Key = g.Key, Count = g.Count() }).ToListAsync();
        AssertCounts(oracle.Select(r => r.Month), monthGroups);
        Assert.Equal(4, monthGroups.Single(g => g.Key.StartsWith("2024-01", StringComparison.Ordinal)).Count);
        AssertExecuted(store, "VARCHAR(100)", "GROUP BY");

        store.Commands.Clear();
        var nullableGroups = await context.Rows.GroupBy(r => r.OptionalStamp.ToString())
            .Select(g => new TextCount { Key = g.Key!, Count = g.Count() }).ToListAsync();
        AssertCounts(oracle.Select(r => r.NullableFallback!), nullableGroups);
        Assert.Single(nullableGroups, g => g.Key == string.Empty && g.Count == 1);
        AssertExecuted(store, "COALESCE(CAST(", "GROUP BY");
    }

    [DamengFact]
    public async Task BoxedNullableDateTimeFailsBeforeSqlWhileDirectNullableStillWorks()
    {
        await using var store = await DateStringStore.CreateAsync();
        store.Commands.Clear();
        var groupError = await Assert.ThrowsAsync<InvalidOperationException>(() => store.Context.Rows
            .GroupBy(r => ((object?)r.OptionalStamp)!.ToString()).Select(g => g.Count()).ToListAsync());
        var filterError = await Assert.ThrowsAsync<InvalidOperationException>(() => store.Context.Rows
            .Where(r => ((object?)r.OptionalStamp)!.ToString() == "").ToListAsync());
        Assert.Contains("boxed DateTime.ToString", groupError.Message, StringComparison.Ordinal);
        Assert.Contains("boxed DateTime.ToString", filterError.Message, StringComparison.Ordinal);
        Assert.Empty(store.Commands);

        Assert.Equal([6], await store.Context.Rows.Where(r => r.OptionalStamp.ToString() == "")
            .Select(r => r.Id).ToListAsync());
        AssertExecuted(store, "COALESCE(CAST(");
    }

    [DamengFact]
    public async Task ObjectValuedExpressionsCannotChangeNullBoxingSemantics()
    {
        await using var store = await DateStringStore.CreateAsync();
        store.Commands.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Context.Rows
            .GroupBy(r => (r.Id > 0 ? (object?)r.OptionalStamp : (object?)r.OptionalStamp)!.ToString())
            .Select(g => g.Count()).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Context.Rows
            .Where(r => ((object?)r.OptionalStamp ?? (object?)r.OptionalStamp)!.ToString() == "").ToListAsync());
        var twoSources = from left in store.Context.Rows
                         from right in store.Context.Rows
                         where ((object?)left.OptionalStamp ?? (object?)right.OptionalStamp)!.ToString() == ""
                         select left.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => twoSources.ToListAsync());
        Assert.Empty(store.Commands);
        Assert.Equal([6], await store.Context.Rows
            .Where(r => (r.Id > 0 ? r.OptionalStamp : null).ToString() == "")
            .Select(r => r.Id).ToListAsync());
        AssertExecuted(store, "COALESCE(CAST(");
    }

    [DamengTheory]
    [InlineData("yyyy")]
    [InlineData("yyyy-MM")]
    [InlineData("yyyy-MM-dd")]
    [InlineData("yyyy-MM-dd HH:mm:ss")]
    [InlineData("yyyy-MM-dd HH:mm:ss.fffffff")]
    [InlineData("s")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss")]
    public async Task ConstantNumericFormatsExecuteAsServerQueries(string format)
    {
        await using var store = await DateStringStore.CreateAsync();
        var selector = FormatSelector(format);
        var formatted = store.Context.Rows.Select(selector);
        var expected = DateStringStore.Seed().Select(r => r.Stamp.ToString(format, CultureInfo.InvariantCulture)).ToList();
        store.Commands.Clear();
        var actual = await formatted.OrderBy(text => text).ToListAsync();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
        AssertExecuted(store, "TO_CHAR(", "ORDER BY");

        store.Commands.Clear();
        var key = expected[0];
        Assert.Equal(expected.Count(text => text == key), await formatted.Where(text => text == key).CountAsync());
        AssertExecuted(store, "TO_CHAR(", "WHERE");

        store.Commands.Clear();
        var groups = await formatted.GroupBy(text => text).Select(g => new TextCount { Key = g.Key, Count = g.Count() }).ToListAsync();
        AssertCounts(expected, groups);
        AssertExecuted(store, "TO_CHAR(", "GROUP BY");
    }
#pragma warning restore CA1305

    private static Expression<Func<DateStringRow, string>> FormatSelector(string format)
    {
        var row = Expression.Parameter(typeof(DateStringRow), "row");
        var call = Expression.Call(Expression.Property(row, nameof(DateStringRow.Stamp)), nameof(DateTime.ToString),
            Type.EmptyTypes, Expression.Constant(format));
        return Expression.Lambda<Func<DateStringRow, string>>(call, row);
    }

    private static void AssertCounts(IEnumerable<string> values, List<TextCount> actual)
    {
        var expected = values.GroupBy(text => text, StringComparer.Ordinal)
            .Select(g => new TextCount { Key = g.Key, Count = g.Count() });
        Assert.Equal(expected.OrderBy(r => r.Key, StringComparer.Ordinal), actual.OrderBy(r => r.Key, StringComparer.Ordinal));
    }

    private static string AssertExecuted(DateStringStore store, params string[] fragments)
    {
        var sql = Assert.Single(store.Commands);
        foreach (var fragment in fragments) Assert.Contains(fragment, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CLOB", sql, StringComparison.Ordinal);
        return sql;
    }

    private sealed record TextCount
    {
        public string Key { get; set; } = "";
        public int Count { get; set; }
    }

    private sealed record TextRow
    {
        public int Id { get; set; }
        public string Stamp { get; set; } = "";
        public string Date { get; set; } = "";
        public string Native { get; set; } = "";
        public string Month { get; set; } = "";
        public string? Nullable { get; set; }
        public string? NullableFallback { get; set; }
    }

    private sealed class DateStringRow
    {
        public int Id { get; set; }
        public DateTime Stamp { get; set; }
        public DateTime DateValue { get; set; }
        public DateTime NativeAt { get; set; }
        public DateTime MonthValue { get; set; }
        public DateTime? OptionalStamp { get; set; }
    }

    private sealed class DateStringContext(DbContextOptions<DateStringContext> options, string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;
        public DbSet<DateStringRow> Rows => Set<DateStringRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<DateStringRow>(entity =>
        {
            entity.ToTable(TableName);
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.Stamp).HasColumnType("TIMESTAMP(7)");
            entity.Property(r => r.DateValue).HasColumnType("DATE");
            entity.Property(r => r.NativeAt).HasColumnType("DATETIME(0)");
            entity.Property(r => r.MonthValue).HasColumnType("TIMESTAMP(7)");
            entity.Property(r => r.OptionalStamp).HasColumnType("TIMESTAMP(7)");
        });
    }

    private sealed class DateStringModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is DateStringContext dates
            ? (context.GetType(), dates.TableName, designTime) : (context.GetType(), designTime);
    }

    private sealed class DateStringStore : IAsyncDisposable
    {
        private readonly string _connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        private readonly string _tableName = "EF10_DS_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        private bool _created;
        public DateStringContext Context { get; private set; } = null!;
        public List<string> Commands { get; } = [];

        public static DateStringRow[] Seed()
        {
            DateTime[] stamps =
            [
                new(2024, 1, 1), new(2024, 1, 1),
                new DateTime(2024, 1, 20, 10, 20, 30).AddTicks(1234567),
                new DateTime(2024, 1, 20, 10, 20, 30).AddTicks(1234568),
                new(2024, 2, 1), new(2024, 2, 15, 11, 12, 13),
                DateTime.MinValue, DateTime.MaxValue
            ];
            return stamps.Select((stamp, index) => new DateStringRow
            {
                Id = index + 1,
                Stamp = stamp,
                DateValue = stamp.Date,
                NativeAt = new DateTime(stamp.Ticks - stamp.Ticks % TimeSpan.TicksPerSecond),
                MonthValue = new DateTime(stamp.Year, stamp.Month, 1),
                OptionalStamp = index == 5 ? null : stamp
            }).ToArray();
        }

        public static async Task<DateStringStore> CreateAsync()
        {
            var store = new DateStringStore();
            try
            {
                await using var connection = new DmConnection(store._connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE "{store._tableName}" (
                        "Id" INT NOT NULL, "Stamp" TIMESTAMP(7) NOT NULL, "DateValue" DATE NOT NULL,
                        "NativeAt" DATETIME(0) NOT NULL, "MonthValue" TIMESTAMP(7) NOT NULL,
                        "OptionalStamp" TIMESTAMP(7),
                        CONSTRAINT "PK_{store._tableName}" NOT CLUSTER PRIMARY KEY ("Id")
                    )
                    """;
                await command.ExecuteNonQueryAsync();
                store._created = true;
                store.Context = new DateStringContext(new DbContextOptionsBuilder<DateStringContext>()
                    .UseDameng(store._connectionString)
                    .ReplaceService<IModelCacheKeyFactory, DateStringModelCacheKeyFactory>()
                    .EnableServiceProviderCaching(false)
                    .AddInterceptors(new CommandCapture(store.Commands)).Options, store._tableName);
                store.Context.Rows.AddRange(Seed());
                await store.Context.SaveChangesAsync();
                store.Context.ChangeTracker.Clear();
                var restored = await store.Context.Rows.OrderBy(r => r.Id).Select(r => r.Stamp).ToListAsync();
                Assert.Equal(Seed().Select(r => r.Stamp), restored);
                store.Commands.Clear();
                return store;
            }
            catch
            {
                await store.DisposeAsync();
                throw;
            }
        }

        public async Task<List<TextRow>> NativeTextAsync()
        {
            await using var connection = new DmConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT "Id", CAST("Stamp" AS VARCHAR(100)), CAST("DateValue" AS VARCHAR(100)),
                    CAST("NativeAt" AS VARCHAR(100)), CAST("MonthValue" AS VARCHAR(100)),
                    CAST("OptionalStamp" AS VARCHAR(100)), COALESCE(CAST("OptionalStamp" AS VARCHAR(100)), '')
                FROM "{_tableName}" ORDER BY "Id"
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<TextRow>();
            while (await reader.ReadAsync()) rows.Add(new TextRow
            {
                Id = reader.GetInt32(0),
                Stamp = reader.GetString(1),
                Date = reader.GetString(2),
                Native = reader.GetString(3),
                Month = reader.GetString(4),
                Nullable = reader.IsDBNull(5) ? null : reader.GetString(5),
                NullableFallback = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
            return rows;
        }

        public async ValueTask DisposeAsync()
        {
            if (Context is not null) await Context.DisposeAsync();
            if (!_created) return;
            await using var connection = new DmConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE \"{_tableName}\"";
            await command.ExecuteNonQueryAsync();
            _created = false;
        }
    }

    private sealed class CommandCapture(List<string> commands) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            commands.Add(command.CommandText);
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
