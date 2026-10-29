#pragma warning disable EF1001 // Tests exercise the actual mapping and migration contracts.
#pragma warning disable EF1002 // SQL interpolates provider-generated literals and unique test identifiers.
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Linq.Expressions;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Plan", "006")]
public sealed class DamengTemporalLiteralFunctionalTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [DamengTheory]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(6, true)]
    public async Task GeneratedTemporalLiteralMatchesExactServerValue(int precision, bool bare)
    {
        await using var context = Context();
        var source = context.GetService<IRelationalTypeMappingSource>();
        foreach (var offset in OffsetModes)
        {
            var mapping = source.FindMapping(offset ? typeof(DateTimeOffset) : typeof(DateTime),
                bare ? offset ? "DATETIME WITH TIME ZONE" : "TIMESTAMP" : null,
                precision: bare ? null : precision)!;
            Assert.Equal("NULL", mapping.GenerateSqlLiteral(null));
            foreach (var date in Values(precision))
            foreach (var zone in (offset ? Offsets : ZeroOffset).Where(z => date.Ticks - z.Ticks >= DateTime.MinValue.Ticks && date.Ticks - z.Ticks <= DateTime.MaxValue.Ticks))
            {
                object value = offset ? (object)new DateTimeOffset(date, zone) : date;
                var literal = mapping.GenerateSqlLiteral(value);
                await AssertExpression(context, literal, value);
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateExplicitParserPreservesTicksAndOffset(bool offset)
    {
        await using var context = Context();
        foreach (var date in Values(7))
        foreach (var zone in (offset ? Offsets : ZeroOffset).Where(z => date.Ticks - z.Ticks >= DateTime.MinValue.Ticks && date.Ticks - z.Ticks <= DateTime.MaxValue.Ticks))
        {
            object value = offset ? (object)new DateTimeOffset(date, zone) : date;
            var text = offset ? ((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            var expression = offset ? $"TO_TIMESTAMP_TZ('{text}', 'YYYY-MM-DD HH24:MI:SS.FF7TZH:TZM')"
                : $"TO_TIMESTAMP('{text}', 'YYYY-MM-DD HH24:MI:SS.FF7')";
            await AssertExpression(context, $"CAST({expression} AS {(offset ? "DATETIME(7) WITH TIME ZONE" : "TIMESTAMP(7)")})", value);
        }
    }

    [DamengTheory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public Task TemporalHasDataAndInsertDataKeepTicksAndOffset(int precision)
        => WithTable(precision, false, true, async context =>
        {
            var at = Sample(precision).AddTicks(precision == 6 ? 10 : 1);
            var zoned = new DateTimeOffset(at, new TimeSpan(-5, -30, 0));
            var model = context.GetService<IDesignTimeModel>().Model;
            await Execute(context, context.GetService<IMigrationsSqlGenerator>().Generate([
                new InsertDataOperation { Table = context.Table, Columns = ["Id", "At", "Zoned"],
                    ColumnTypes = ["INT", context.AtType, context.ZonedType], Values = new object[,] { { 2, at, zoned } } }
            ], model));
            var rows = await context.Rows.OrderBy(r => r.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            AssertValue(Sample(precision), new DateTimeOffset(Sample(precision), TimeSpan.FromHours(8)), rows[0]);
            AssertValue(at, zoned, rows[1]);
            await AssertStored(context, 1, Sample(precision), new DateTimeOffset(Sample(precision), TimeSpan.FromHours(8)));
            await AssertStored(context, 2, at, zoned);
        });

    [DamengTheory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public Task TemporalDefaultValuesKeepTicksAndOffset(int precision)
        => WithTable(precision, true, false, async context =>
        {
            await context.Database.ExecuteSqlRawAsync($"INSERT INTO \"{context.Table}\" (\"Id\") VALUES (1)");
            var at = Sample(precision); var zoned = new DateTimeOffset(at, TimeSpan.FromHours(8));
            AssertValue(at, zoned, await context.Rows.SingleAsync());
            await AssertStored(context, 1, at, zoned);
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate([
                new AddColumnOperation { Table = context.Table, Name = "ExtraAt", ClrType = typeof(DateTime),
                    ColumnType = context.AtType, DefaultValue = at },
                new AddColumnOperation { Table = context.Table, Name = "ExtraZoned", ClrType = typeof(DateTimeOffset),
                    ColumnType = context.ZonedType, DefaultValue = zoned }
            ], context.GetService<IDesignTimeModel>().Model);
            Assert.All(commands, c => Assert.True(c.TransactionSuppressed));
            await Execute(context, commands);
            Assert.Equal(at.Ticks, (await context.Database.SqlQueryRaw<DateTime>($"SELECT \"ExtraAt\" AS \"Value\" FROM \"{context.Table}\"").SingleAsync()).Ticks);
            var extra = await context.Database.SqlQueryRaw<DateTimeOffset>($"SELECT \"ExtraZoned\" AS \"Value\" FROM \"{context.Table}\"").SingleAsync();
            Assert.Equal(zoned.Ticks, extra.Ticks); Assert.Equal(zoned.Offset, extra.Offset); Assert.Equal(zoned.UtcTicks, extra.UtcTicks);
        });

    [DamengTheory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public Task ParameterizedAndConstantTemporalPredicatesAgree(int precision)
        => WithTable(precision, false, false, async context =>
        {
#if WDRIVER
            var at = Sample(precision);
            var adjacentTicks = precision == 6 ? 10 : 1;
#else
            // The pinned official driver's non-microsecond parameter defect is a documented
            // maintainer-accepted risk. This agreement control uses representable input.
            var at = Sample(6);
            const int adjacentTicks = 10;
#endif
            var zoned = new DateTimeOffset(at, TimeSpan.FromHours(8));
            context.Rows.Add(new TemporalRow { Id = 1, At = at, Zoned = zoned });
            context.Rows.Add(new TemporalRow { Id = 2, At = at.AddTicks(adjacentTicks), Zoned = zoned.AddTicks(adjacentTicks) });
            context.Rows.Add(new TemporalRow { Id = 3 });
            await context.SaveChangesAsync(); context.ChangeTracker.Clear();
            Assert.Null((await context.Rows.SingleAsync(r => r.Id == 3)).At);
            Assert.Null((await context.Rows.SingleAsync(r => r.Id == 3)).Zoned);
            foreach (var offset in OffsetModes)
            foreach (var operation in Comparisons)
            {
                var row = Expression.Parameter(typeof(TemporalRow), "r");
                var member = Expression.Property(row, offset ? nameof(TemporalRow.Zoned) : nameof(TemporalRow.At));
                object value = offset ? (object)zoned : at;
                var body = Expression.MakeBinary(operation, member, Expression.Constant(value, member.Type));
                var constantQuery = context.Rows.Where(Expression.Lambda<Func<TemporalRow, bool>>(body, row)).OrderBy(r => r.Id).Select(r => r.Id);
                var parameterQuery = offset
                    ? operation switch {
                        ExpressionType.Equal => context.Rows.Where(r => r.Zoned == zoned),
                        ExpressionType.NotEqual => context.Rows.Where(r => r.Zoned != zoned),
                        ExpressionType.LessThan => context.Rows.Where(r => r.Zoned < zoned),
                        _ => context.Rows.Where(r => r.Zoned > zoned) }
                    : operation switch {
                        ExpressionType.Equal => context.Rows.Where(r => r.At == at),
                        ExpressionType.NotEqual => context.Rows.Where(r => r.At != at),
                        ExpressionType.LessThan => context.Rows.Where(r => r.At < at),
                        _ => context.Rows.Where(r => r.At > at) };
                context.Commands.Commands.Clear(); context.Commands.ParameterCounts.Clear();
                var constants = await constantQuery.ToListAsync();
                var constantSql = Assert.Single(context.Commands.Commands);
                Assert.Contains("TO_TIMESTAMP", constantSql, StringComparison.Ordinal);
                Assert.Equal(0, Assert.Single(context.Commands.ParameterCounts));
                context.Commands.Commands.Clear(); context.Commands.ParameterCounts.Clear();
                var parameters = await parameterQuery.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();
                Assert.Contains(":", Assert.Single(context.Commands.Commands), StringComparison.Ordinal);
                Assert.True(Assert.Single(context.Commands.ParameterCounts) > 0);
                int[] expected = operation switch { ExpressionType.Equal => [1], ExpressionType.NotEqual => [2, 3], ExpressionType.LessThan => [], _ => [2] };
                output.WriteLine($"p={precision}; offset={offset}; op={operation}; constant={string.Join(",", constants)}; parameter={string.Join(",", parameters)}");
                Assert.Equal(expected, constants); Assert.Equal(expected, parameters);
            }
        });

    [DamengTheory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public Task DriverTemporalParameterPrecisionRiskIsCharacterized(int precision)
        => WithTable(precision, false, true, async context =>
        {
            var at = Sample(precision);
            await AssertStored(context, 1, at, new DateTimeOffset(at, TimeSpan.FromHours(8)));
            var row = Expression.Parameter(typeof(TemporalRow), "r");
            var constant = Expression.Equal(Expression.Property(row, nameof(TemporalRow.At)),
                Expression.Constant(at, typeof(DateTime?)));
            var constantQuery = context.Rows.Where(Expression.Lambda<Func<TemporalRow, bool>>(constant, row)).Select(r => r.Id);
            context.Commands.Commands.Clear(); context.Commands.ParameterCounts.Clear();
            Assert.Equal([1], await constantQuery.ToListAsync());
            Assert.Contains("TO_TIMESTAMP(", Assert.Single(context.Commands.Commands), StringComparison.Ordinal);
            Assert.Equal(0, Assert.Single(context.Commands.ParameterCounts));

            context.Commands.Commands.Clear(); context.Commands.ParameterCounts.Clear();
            var parameters = await context.Rows.Where(r => r.At == at).Select(r => r.Id).ToListAsync();
            Assert.Single(context.Commands.Commands);
            Assert.True(Assert.Single(context.Commands.ParameterCounts) > 0);
#if WDRIVER
            Assert.Equal([1], parameters);
            output.WriteLine($"Driver=W; targetPrecision={precision}; constant=1; parameter=1; exact tick control passed.");
#else
            // Characterize the known defect of the locked driver, not a supported semantic.
            // If the driver is fixed, update this expectation and the public known-risk note.
            Assert.Empty(parameters);
            output.WriteLine($"Driver=Official; targetPrecision={precision}; constant=1; parameter=empty; KNOWN DRIVER RISK accepted by maintainer; not a precision guarantee.");
#endif
        });

    [DamengFact]
    public void TemporalLiteralRejectsUnrepresentableTargetPrecision()
    {
        using var context = Context();
        var source = context.GetService<IRelationalTypeMappingSource>();
        Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(DateTime), "TIMESTAMP(6)")!.GenerateSqlLiteral(Sample(7)));
        Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(DateTimeOffset), "DATETIME WITH TIME ZONE")!
            .GenerateSqlLiteral(new DateTimeOffset(Sample(7), TimeSpan.Zero)));
    }

    private static readonly ExpressionType[] Comparisons = [ExpressionType.Equal, ExpressionType.NotEqual, ExpressionType.LessThan, ExpressionType.GreaterThan];
    private static DateTime Sample(int precision) => new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(precision == 6 ? 1234560 : 1234567);
    private static void AssertValue(DateTime at, DateTimeOffset zoned, TemporalRow actual)
    {
        Assert.Equal(at.Ticks, actual.At!.Value.Ticks);
        Assert.Equal(zoned.Ticks, actual.Zoned!.Value.Ticks); Assert.Equal(zoned.Offset, actual.Zoned.Value.Offset); Assert.Equal(zoned.UtcTicks, actual.Zoned.Value.UtcTicks);
    }
    private static async Task AssertStored(TemporalContext context, int id, DateTime at, DateTimeOffset zoned)
    {
        var dt = await context.Database.SqlQueryRaw<string>($"SELECT TO_CHAR(\"At\", 'YYYY-MM-DD HH24:MI:SS.FF7') AS \"Value\" FROM \"{context.Table}\" WHERE \"Id\" = {id}").SingleAsync();
        var dto = await context.Database.SqlQueryRaw<string>($"SELECT TO_CHAR(\"Zoned\", 'YYYY-MM-DD HH24:MI:SS.FF7TZH:TZM') AS \"Value\" FROM \"{context.Table}\" WHERE \"Id\" = {id}").SingleAsync();
        Assert.Equal(at.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture), dt);
        Assert.Equal(zoned.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture), dto);
    }
    private static async Task WithTable(int precision, bool defaults, bool seed, Func<TemporalContext, Task> test)
    {
        var table = "EF10_TLF_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var capture = new Capture();
        await using var context = new TemporalContext(new DbContextOptionsBuilder<TemporalContext>()
            .UseDamengUnderTest(DamengTestEnvironment.GetRequiredConnectionString())
            .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(capture).Options, table, precision, defaults, seed, capture);
        try
        {
            var model = context.GetService<IDesignTimeModel>().Model;
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
            Assert.Equal(seed, operations.Any(o => o is InsertDataOperation));
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
            Assert.All(commands.Where(c => c.CommandText.StartsWith("CREATE", StringComparison.Ordinal)), c => Assert.True(c.TransactionSuppressed));
            await Execute(context, commands);
            await test(context);
        }
        finally
        {
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :name";
            var parameter = command.CreateParameter(); parameter.ParameterName = "name"; parameter.Value = table; command.Parameters.Add(parameter);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
                await context.Database.ExecuteSqlRawAsync($"DROP TABLE \"{table}\"");
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
    }
    private static async Task Execute(DbContext context, IReadOnlyList<MigrationCommand> commands)
    {
        foreach (var command in commands) await context.Database.ExecuteSqlRawAsync(command.CommandText);
    }
    private sealed class TemporalContext(DbContextOptions<TemporalContext> options, string table, int precision, bool defaults, bool seed, Capture commands) : DbContext(options)
    {
        public string Table { get; } = table;
        public int Precision { get; } = precision;
        public bool Defaults { get; } = defaults;
        public bool Seed { get; } = seed;
        public string AtType => $"TIMESTAMP({Precision})";
        public string ZonedType => $"DATETIME({Precision}) WITH TIME ZONE";
        public Capture Commands { get; } = commands;
        public DbSet<TemporalRow> Rows => Set<TemporalRow>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<TemporalRow>(e => {
                e.ToTable(Table); e.Property(r => r.Id).ValueGeneratedNever();
                e.Property(r => r.At).HasColumnType(AtType);
                e.Property(r => r.Zoned).HasColumnType(ZonedType);
                if (Defaults) { e.Property(r => r.At).HasDefaultValue(Sample(Precision)); e.Property(r => r.Zoned).HasDefaultValue(new DateTimeOffset(Sample(Precision), TimeSpan.FromHours(8))); }
                if (Seed) e.HasData(new TemporalRow { Id = 1, At = Sample(Precision), Zoned = new DateTimeOffset(Sample(Precision), TimeSpan.FromHours(8)) });
            });
        }
    }
    private sealed class TemporalRow
    {
        public int Id { get; set; }
        public DateTime? At { get; set; }
        public DateTimeOffset? Zoned { get; set; }
    }
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is TemporalContext c ? (context.GetType(), c.Table, c.Precision, c.Defaults, c.Seed, designTime) : (object)(context.GetType(), designTime);
    }
    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public List<int> ParameterCounts { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); ParameterCounts.Add(command.Parameters.Count); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private static readonly bool[] OffsetModes = [false, true];
    private static readonly TimeSpan[] Offsets = [TimeSpan.Zero, TimeSpan.FromHours(8), new(-5, -30, 0)];
    private static readonly TimeSpan[] ZeroOffset = [TimeSpan.Zero];
    private static IEnumerable<DateTime> Values(int precision)
    {
        var quantum = precision < 7 ? 10 : 1;
        yield return new DateTime(2024, 2, 29, 13, 14, 15);
        yield return new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(1234560);
        yield return new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(precision < 7 ? 1234560 : 1234567);
        yield return new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(precision < 7 ? 1234570 : 1234568);
        yield return DateTime.MinValue;
        yield return DateTime.MaxValue.AddTicks(-(quantum - 1));
    }
    private static DbContext Context() => new(new DbContextOptionsBuilder()
        .UseDamengUnderTest(DamengTestEnvironment.GetRequiredConnectionString()).Options);
    private static async Task AssertExpression(DbContext context, string expression, object value)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        var offset = value is DateTimeOffset;
        command.CommandText = $"SELECT TO_CHAR({expression}, 'YYYY-MM-DD HH24:MI:SS.FF7{(offset ? "TZH:TZM" : "")}') FROM dual";
        var expected = offset ? ((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)
            : ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        Assert.Equal(expected, Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        if (offset)
        {
            var actual = await context.Database.SqlQueryRaw<DateTimeOffset>($"SELECT {expression} AS \"Value\" FROM dual").SingleAsync();
            var date = (DateTimeOffset)value;
            Assert.Equal(date.Ticks, actual.Ticks); Assert.Equal(date.Offset, actual.Offset); Assert.Equal(date.UtcTicks, actual.UtcTicks);
        }
        else
            Assert.Equal(((DateTime)value).Ticks, (await context.Database.SqlQueryRaw<DateTime>($"SELECT {expression} AS \"Value\" FROM dual").SingleAsync()).Ticks);
    }
}
