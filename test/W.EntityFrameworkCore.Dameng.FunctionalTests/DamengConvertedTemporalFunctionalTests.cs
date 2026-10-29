#pragma warning disable EF1001 // Characterization inspects the real mapping pipeline.
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengConvertedTemporalFunctionalTests(ITestOutputHelper output)
{
    [DamengFact]
    public Task NonNativeTemporalOperationsAreRejectedBeforeExecution()
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = await Create(store, commands);
            foreach (var property in Properties.Where(NonNative))
            foreach (var operation in Zoned(property) ? OffsetOperations : DateOperations)
            {
                commands.Commands.Clear();
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Filter(context, property, operation).ToListAsync());
                Assert.Contains("native temporal", error.Message, StringComparison.Ordinal);
                Assert.Empty(commands.Commands);
                var grouping = await Assert.ThrowsAsync<InvalidOperationException>(() => Group(context, property, operation).ToListAsync());
                Assert.Contains("native temporal", grouping.Message, StringComparison.Ordinal);
                Assert.Empty(commands.Commands);
                output.WriteLine($"{property}.{operation}: rejected; commands=0");
            }
        });

    [DamengFact]
    public Task NativeTemporalControlsMatchClr()
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = await Create(store, commands);
            foreach (var property in Properties.Where(p => !NonNative(p)))
            foreach (var operation in Zoned(property) ? OffsetOperations : DateOperations)
            {
                commands.Commands.Clear();
                Assert.Equal(Expected(property, operation, Stamp), Assert.Single(await Project(context, property, operation).ToListAsync()));
                Assert.Contains(operation is "AddDays" or "AddMonths" ? "DATEADD(" : operation is "Year" or "Month" or "Day" or "Hour" or "Minute" or "Second" or "DamengQuarter" ? "DATEPART(" : "TRUNC(", Assert.Single(commands.Commands), StringComparison.Ordinal);
                Assert.Equal(ExpectedIds(property, operation), await Filter(context, property, operation).OrderBy(id => id).ToListAsync());
                var groupExpected = Enumerable.Range(1, 3).GroupBy(id => property.EndsWith('N') && id == 3 ? null
                    : Expected(property, operation, id == 1 ? Stamp : id == 2 ? new DateTime(2025, 1, 1, 23, 59, 58) : Stamp.AddDays(2)))
                    .Select(g => g.Count()).Order();
                Assert.Equal(groupExpected, (await Group(context, property, operation).ToListAsync()).Order());
            }
        });

    [DamengFact]
    public Task NativeTemporalFractionalControlsPreserveVerifiedPrecision()
        => Store.WithTableAsync(async store =>
        {
            var commands = new CommandCaptureInterceptor();
            await using var context = await Create(store, commands);
            var row = await context.Rows.SingleAsync(r => r.Id == 1);
            var fraction = Stamp.AddTicks(1234560);
            row.NativeAt = fraction; row.NativeAtN = fraction; row.IdentityAt = fraction; row.IdentityAtN = fraction;
            row.NativeZoned = new DateTimeOffset(fraction, new TimeSpan(-5, -30, 0)); row.NativeZonedN = row.NativeZoned;
            await context.SaveChangesAsync(); context.ChangeTracker.Clear();
            foreach (var property in Properties.Where(p => !NonNative(p)))
            foreach (var operation in Zoned(property) ? OffsetOperations : DateOperations)
            {
                commands.Commands.Clear();
                Assert.Equal(Expected(property, operation, fraction), Assert.Single(await Project(context, property, operation).ToListAsync()));
                Assert.Single(commands.Commands);
            }
        });

    private static IEnumerable<int> ExpectedIds(string property, string operation)
        => Enumerable.Range(1, 3).Where(id => (!property.EndsWith('N') || id != 3)
            && Equals(Expected(property, operation, id == 1 ? Stamp : id == 2 ? new DateTime(2025, 1, 1, 23, 59, 58) : Stamp.AddDays(2)), Expected(property, operation, Stamp)));

    private static async Task<Context> Create(Store store, CommandCaptureInterceptor commands)
    {
        var context = new Context(new DbContextOptionsBuilder<Context>().UseDamengUnderTest(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(commands).Options, store.TableName);
        context.Rows.AddRange(MakeRow(1, Stamp, false), MakeRow(2, new DateTime(2025, 1, 1, 23, 59, 58), false), MakeRow(3, Stamp.AddDays(2), true));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var actual = await context.Rows.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(3, actual.Count);
        foreach (var row in actual)
        {
            var expected = row.Id == 1 ? Stamp : row.Id == 2 ? new DateTime(2025, 1, 1, 23, 59, 58) : Stamp.AddDays(2);
            Assert.Equal(expected.Ticks, row.NativeAt.Ticks); Assert.Equal(expected, row.IdentityAt);
            Assert.Equal(expected, row.LongAt); Assert.Equal(expected, row.TextAt);
            var zoned = new DateTimeOffset(expected, new TimeSpan(-5, -30, 0));
            foreach (var value in new[] { row.NativeZoned, row.LongZoned, row.TextZoned })
            { Assert.Equal(zoned.Ticks, value.Ticks); Assert.Equal(zoned.Offset, value.Offset); Assert.Equal(zoned.UtcTicks, value.UtcTicks); }
            if (row.Id == 3)
            {
                Assert.Null(row.NativeAtN); Assert.Null(row.IdentityAtN); Assert.Null(row.LongAtN); Assert.Null(row.TextAtN);
                Assert.Null(row.NativeZonedN); Assert.Null(row.LongZonedN); Assert.Null(row.TextZonedN);
            }
            else
            {
                Assert.Equal(expected, row.NativeAtN); Assert.Equal(expected, row.IdentityAtN); Assert.Equal(expected, row.LongAtN); Assert.Equal(expected, row.TextAtN);
                Assert.Equal(zoned.Offset, row.NativeZonedN!.Value.Offset); Assert.Equal(zoned.Offset, row.LongZonedN!.Value.Offset); Assert.Equal(zoned.Offset, row.TextZonedN!.Value.Offset);
                Assert.Equal(zoned.Ticks, row.NativeZonedN.Value.Ticks); Assert.Equal(zoned.Ticks, row.LongZonedN.Value.Ticks); Assert.Equal(zoned.Ticks, row.TextZonedN.Value.Ticks);
            }
        }
        return context;
    }
    private static Row MakeRow(int id, DateTime at, bool nulls)
    {
        var zoned = new DateTimeOffset(at, new TimeSpan(-5, -30, 0));
        return new Row { Id = id, NativeAt = at, IdentityAt = at, LongAt = at, TextAt = at,
            NativeAtN = nulls ? null : at, IdentityAtN = nulls ? null : at, LongAtN = nulls ? null : at, TextAtN = nulls ? null : at,
            NativeZoned = zoned, LongZoned = zoned, TextZoned = zoned,
            NativeZonedN = nulls ? null : zoned, LongZonedN = nulls ? null : zoned, TextZonedN = nulls ? null : zoned };
    }
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Table, designTime);
    }
    private static readonly string[] DateOperations = ["Year", "Date", "AddDays", "AddMonths", "DamengTruncateHour", "DamengTruncateMinute", "DamengStartOfIsoWeek", "DamengQuarter"];
    private static readonly string[] OffsetOperations = ["Year", "Month", "Day", "Hour", "Minute", "Second"];
    private static readonly string[] Properties = ["NativeAt", "NativeAtN", "IdentityAt", "IdentityAtN", "LongAt", "LongAtN", "TextAt", "TextAtN", "NativeZoned", "NativeZonedN", "LongZoned", "LongZonedN", "TextZoned", "TextZonedN"];
    private static bool NonNative(string property) => property.StartsWith("Long", StringComparison.Ordinal) || property.StartsWith("Text", StringComparison.Ordinal);
    private static bool Zoned(string property) => property.Contains("Zoned", StringComparison.Ordinal);
    private static Expression Operation(ParameterExpression row, string property, string operation)
    {
        Expression value = Expression.Property(row, property);
        if (operation.StartsWith("Dameng", StringComparison.Ordinal))
            return Expression.Call(typeof(DamengDateTimeDbFunctionsExtensions).GetMethod(operation, [typeof(DbFunctions), value.Type])!, Expression.Property(null, typeof(EF), nameof(EF.Functions)), value);
        if (Nullable.GetUnderlyingType(value.Type) is not null) value = Expression.Property(value, "Value");
        return operation switch {
            "AddDays" => Expression.Call(value, nameof(DateTime.AddDays), null, Expression.Constant(1d)),
            "AddMonths" => Expression.Call(value, nameof(DateTime.AddMonths), null, Expression.Constant(1)),
            _ => Expression.Property(value, operation) };
    }
    private static IQueryable<int> Filter(Context context, string property, string operation)
    {
        var row = Expression.Parameter(typeof(Row), "r");
        var body = Operation(row, property, operation);
        var expected = Expected(property, operation, Stamp);
        var compare = Expression.Equal(body, Expression.Constant(expected, body.Type));
        return context.Rows.Where(Expression.Lambda<Func<Row, bool>>(compare, row)).Select(r => r.Id);
    }
    private static IQueryable<int> Group(Context context, string property, string operation)
    {
        var row = Expression.Parameter(typeof(Row), "r");
        var key = Expression.Lambda<Func<Row, object>>(Expression.Convert(Operation(row, property, operation), typeof(object)), row);
        return context.Rows.GroupBy(key).Select(g => g.Count());
    }
    private static IQueryable<object> Project(Context context, string property, string operation)
    {
        var row = Expression.Parameter(typeof(Row), "r");
        return context.Rows.Where(r => r.Id == 1).Select(Expression.Lambda<Func<Row, object>>(Expression.Convert(Operation(row, property, operation), typeof(object)), row));
    }
    private static object Expected(string property, string operation, DateTime at)
    {
        if (Zoned(property)) return operation switch { "Year" => at.Year, "Month" => at.Month, "Day" => at.Day, "Hour" => at.Hour, "Minute" => at.Minute, _ => at.Second };
        return operation switch {
            "Year" => at.Year, "Date" => (object)at.Date,
            "AddDays" => at.AddDays(1), "AddMonths" => at.AddMonths(1),
            "DamengTruncateHour" => new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0),
            "DamengTruncateMinute" => new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0),
            "DamengStartOfIsoWeek" => at.Date.AddDays(-(((int)at.DayOfWeek + 6) % 7)),
            _ => (at.Month + 2) / 3 };
    }
    private static readonly DateTime Stamp = new(2024, 2, 29, 13, 14, 15);
    private static string Encode(DateTime value) => "D:" + value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTime Decode(string value) => DateTime.Parse(value[2..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string EncodeOffset(DateTimeOffset value) => "Z:" + value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset DecodeOffset(string value) => DateTimeOffset.Parse(value[2..], CultureInfo.InvariantCulture);
    private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
    {
        public string Table { get; } = table;
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Row>(e => {
                e.ToTable(Table); e.Property(r => r.Id).ValueGeneratedNever();
                e.Property(r => r.NativeAt).HasColumnType(" TIMESTAMP(7) ").HasPrecision(7); e.Property(r => r.NativeAtN).HasPrecision(7);
                e.Property(r => r.IdentityAt).HasConversion(v => v, v => v).HasPrecision(7);
                e.Property(r => r.IdentityAtN).HasConversion(v => v, v => v).HasPrecision(7);
                e.Property(r => r.LongAt).HasConversion<long>(); e.Property(r => r.LongAtN).HasConversion<long>();
                e.Property(r => r.TextAt).HasConversion(v => Encode(v), v => Decode(v)).HasMaxLength(200);
                e.Property(r => r.TextAtN).HasConversion<string>(v => Encode(v!.Value), v => Decode(v)).HasMaxLength(200);
                e.Property(r => r.NativeZoned).HasColumnType(" DATETIME(7) WITH TIME ZONE ").HasPrecision(7); e.Property(r => r.NativeZonedN).HasConversion(v => v, v => v).HasPrecision(7);
                e.Property(r => r.LongZoned).HasConversion<long>(); e.Property(r => r.LongZonedN).HasConversion<long>();
                e.Property(r => r.TextZoned).HasConversion(v => EncodeOffset(v), v => DecodeOffset(v)).HasMaxLength(200);
                e.Property(r => r.TextZonedN).HasConversion<string>(v => EncodeOffset(v!.Value), v => DecodeOffset(v)).HasMaxLength(200);
            });
        }
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime NativeAt { get; set; }
        public DateTime? NativeAtN { get; set; }
        public DateTime IdentityAt { get; set; }
        public DateTime? IdentityAtN { get; set; }
        public DateTime LongAt { get; set; }
        public DateTime? LongAtN { get; set; }
        public DateTime TextAt { get; set; }
        public DateTime? TextAtN { get; set; }
        public DateTimeOffset NativeZoned { get; set; }
        public DateTimeOffset? NativeZonedN { get; set; }
        public DateTimeOffset LongZoned { get; set; }
        public DateTimeOffset? LongZonedN { get; set; }
        public DateTimeOffset TextZoned { get; set; }
        public DateTimeOffset? TextZonedN { get; set; }
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
            TableName = $"EF10_CT_{suffix}";
            PrimaryKeyName = $"PK_CT_{suffix}";
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
                    "NativeAt" TIMESTAMP(7) NOT NULL,
                    "NativeAtN" TIMESTAMP(7) NULL,
                    "IdentityAt" TIMESTAMP(7) NOT NULL,
                    "IdentityAtN" TIMESTAMP(7) NULL,
                    "LongAt" BIGINT NOT NULL,
                    "LongAtN" BIGINT NULL,
                    "TextAt" NVARCHAR2(200) NOT NULL,
                    "TextAtN" NVARCHAR2(200) NULL,
                    "NativeZoned" DATETIME(7) WITH TIME ZONE NOT NULL,
                    "NativeZonedN" DATETIME(7) WITH TIME ZONE NULL,
                    "LongZoned" BIGINT NOT NULL,
                    "LongZonedN" BIGINT NULL,
                    "TextZoned" NVARCHAR2(200) NOT NULL,
                    "TextZonedN" NVARCHAR2(200) NULL,
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
