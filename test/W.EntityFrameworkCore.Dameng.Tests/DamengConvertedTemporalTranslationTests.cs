#pragma warning disable EF1001 // Tests inspect effective mappings and SQL pipeline contracts.
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengConvertedTemporalTranslationTests(ITestOutputHelper output)
{
    [Fact]
    public void ConvertedTemporalBaselineIsCharacterized()
    {
        using var context = Create();
        foreach (var property in Properties)
        {
            var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetRelationalTypeMapping();
            output.WriteLine($"{property}: CLR={mapping.ClrType}; store={mapping.StoreType}; converter={mapping.Converter?.GetType().Name}; provider={mapping.Converter?.ProviderClrType}");
            foreach (var operation in Zoned(property) ? OffsetOperations : DateOperations)
            {
                try { output.WriteLine($"{property}.{operation}: " + Filter(context, property, operation).ToQueryString()); }
                catch (InvalidOperationException error) { output.WriteLine($"{property}.{operation}: translation: {error.Message}"); }
            }
        }
    }
    [Fact]
    public void NonNativeTemporalOperationsAreRejectedBeforeExecution()
    {
        using var context = Create();
        foreach (var property in Properties.Where(NonNative))
        foreach (var operation in Zoned(property) ? OffsetOperations : DateOperations)
        {
            var error = Assert.Throws<InvalidOperationException>(() => Filter(context, property, operation).ToQueryString());
            Assert.Contains("native temporal", error.Message, StringComparison.Ordinal);
            var grouping = Assert.Throws<InvalidOperationException>(() => Group(context, property, operation).ToQueryString());
            Assert.Contains("native temporal", grouping.Message, StringComparison.Ordinal);
        }
    }
    [Fact]
    public void NativeTemporalControlsRemainServerTranslated()
    {
        using var context = Create();
        foreach (var property in Properties.Where(p => !NonNative(p)))
        foreach (var operation in Zoned(property) ? OffsetOperations : DateOperations)
        {
            var sql = Filter(context, property, operation).ToQueryString();
            Assert.Contains("GROUP BY", Group(context, property, operation).ToQueryString(), StringComparison.Ordinal);
            Assert.Contains(operation is "AddDays" or "AddMonths" ? "DATEADD(" : operation is "Year" or "Month" or "Day" or "Hour" or "Minute" or "Second" or "DamengQuarter" ? "DATEPART(" : "TRUNC(", sql, StringComparison.Ordinal);
        }
        Assert.Contains("LOCALTIMESTAMP", context.Rows.Where(r => r.NativeAt < DateTime.Now).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("SYS_EXTRACT_UTC", context.Rows.Where(r => r.NativeAt < DateTime.UtcNow).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("TRUNC(", context.Rows.Where(r => r.NativeAt < DateTime.Today).ToQueryString(), StringComparison.Ordinal);
    }
    [Fact]
    public void NativeTemporalStoreTypeWhitespaceIsAccepted()
    {
        using var context = new WhitespaceContext(new DbContextOptionsBuilder<WhitespaceContext>()
            .UseDameng("Server=localhost;User=test;Password=test").Options);
        Assert.Contains("DATEPART(", context.Rows.Where(r => r.NativeAt.Year == 2024).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("TRUNC(", context.Rows.Where(r => r.NativeAt.Date == Stamp.Date).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("DATEADD(", context.Rows.Where(r => r.NativeAt.AddDays(1) == Stamp).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("DATEPART(", context.Rows.Where(r => r.NativeZoned.Year == 2024).ToQueryString(), StringComparison.Ordinal);
    }
    [Fact]
    public void NullableIdentityTemporalMappingsRemainNative()
    {
        using var context = new WhitespaceContext(new DbContextOptionsBuilder<WhitespaceContext>()
            .UseDameng("Server=localhost;User=test;Password=test").Options);
        Assert.Contains("DATEPART(", context.Rows.Where(r => r.NativeAtN!.Value.Year == 2024).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("DATEPART(", context.Rows.Where(r => r.NativeZonedN!.Value.Year == 2024).ToQueryString(), StringComparison.Ordinal);
    }
    private sealed class WhitespaceContext(DbContextOptions<WhitespaceContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Row>().Property(r => r.NativeAtN).HasConversion(v => v, v => v).HasPrecision(7);
            builder.Entity<Row>().Property(r => r.NativeZonedN).HasConversion(v => v, v => v).HasPrecision(7);
            builder.Entity<Row>().Property(r => r.NativeAt).HasColumnType(" TIMESTAMP(7) ");
            builder.Entity<Row>().Property(r => r.NativeZoned).HasColumnType(" DATETIME(7) WITH TIME ZONE ");
        }
    }
    private static Context Create() => new(new DbContextOptionsBuilder<Context>().UseDameng("Server=localhost;User=test;Password=test").Options, "CONVERTED_CASES");
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
                e.Property(r => r.NativeAt).HasPrecision(7); e.Property(r => r.NativeAtN).HasPrecision(7);
                e.Property(r => r.IdentityAt).HasConversion(v => v, v => v).HasPrecision(7);
                e.Property(r => r.IdentityAtN).HasConversion<DateTime>(v => v!.Value, v => v).HasPrecision(7);
                e.Property(r => r.LongAt).HasConversion<long>(); e.Property(r => r.LongAtN).HasConversion<long>();
                e.Property(r => r.TextAt).HasConversion(v => Encode(v), v => Decode(v)).HasMaxLength(200);
                e.Property(r => r.TextAtN).HasConversion<string>(v => Encode(v!.Value), v => Decode(v)).HasMaxLength(200);
                e.Property(r => r.NativeZoned).HasPrecision(7); e.Property(r => r.NativeZonedN).HasPrecision(7);
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
}
