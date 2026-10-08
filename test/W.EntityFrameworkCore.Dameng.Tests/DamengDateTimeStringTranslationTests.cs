using System.Globalization;
using Microsoft.EntityFrameworkCore;
using W.EntityFrameworkCore.Dameng.TestUtilities;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDateTimeStringTranslationTests
{
#pragma warning disable CA1305 // Verify the server-side overloads without introducing a culture argument.
    [Fact]
    public void DefaultConversionWorksInProjectionFilterOrderAndGroup()
    {
        using var context = CreateContext();
        var key = "2024-01-01 00:00:00";
        var projection = context.Rows.Select(r => r.At!.Value.ToString()).ToQueryString();
        var filter = context.Rows.Where(r => r.At!.Value.ToString() == key).ToQueryString();
        var order = context.Rows.OrderBy(r => r.At!.Value.ToString()).ToQueryString();
        var group = context.Rows.GroupBy(r => r.At!.Value.ToString()).Select(g => new { g.Key, Count = g.Count() }).ToQueryString();
        foreach (var sql in new[] { projection, filter, order, group })
        {
            Assert.Contains("AS VARCHAR(100)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("CLOB", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("TRUNC(", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        }
        Assert.Contains("WHERE", filter, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", order, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", group, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableToStringKeepsEmptyStringFallback()
    {
        using var context = CreateContext();
        var sql = context.Rows.GroupBy(r => r.At.ToString()).Select(g => new { g.Key, Count = g.Count() }).ToQueryString();
        Assert.Contains("COALESCE(CAST(", sql, StringComparison.Ordinal);
        Assert.Contains("VARCHAR(100)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CLOB", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxedNullableDateTimeDoesNotAcquireEmptyStringSemantics()
    {
        using var context = CreateContext();
        DateTime? empty = null;
        Assert.Throws<NullReferenceException>(() => ((object?)empty)!.ToString());
        var groupError = Assert.Throws<InvalidOperationException>(() => context.Rows
            .GroupBy(r => ((object?)r.At)!.ToString()).Select(g => g.Count()).ToQueryString());
        var filterError = Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(r => ((object?)r.At)!.ToString() == "").ToQueryString());
        Assert.Contains("boxed DateTime.ToString", groupError.Message, StringComparison.Ordinal);
        Assert.Contains("boxed DateTime.ToString", filterError.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(r => (r.At as object)!.ToString() == "").ToQueryString());
    }

    [Fact]
    public void ObjectValuedConditionalAndCoalesceCannotHideDateTimeBoxing()
    {
        using var context = CreateContext();
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .GroupBy(r => (r.Id > 0 ? (object?)r.At : (object?)r.At)!.ToString())
            .Select(g => g.Count()).ToQueryString());
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(r => ((object?)r.At ?? (object?)r.At)!.ToString() == "").ToQueryString());
        var twoSources = from left in context.Rows
                         from right in context.Rows
                         where ((object?)left.At ?? (object?)right.At)!.ToString() == ""
                         select left.Id;
        Assert.Throws<InvalidOperationException>(() => twoSources.ToQueryString());
        var directNullable = context.Rows.GroupBy(r => r.Id > 0 ? r.At : null)
            .Select(g => g.Key.ToString()).ToQueryString();
        Assert.Contains("COALESCE(CAST(", directNullable, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yyyy", "YYYY")]
    [InlineData("yyyy-MM", "YYYY-MM")]
    [InlineData("yyyy-MM-dd", "YYYY-MM-DD")]
    [InlineData("yyyy-MM-dd HH:mm:ss", "YYYY-MM-DD HH24:MI:SS")]
    [InlineData("yyyy-MM-dd HH:mm:ss.fffffff", "YYYY-MM-DD HH24:MI:SS.FF7")]
    [InlineData("s", "YYYY-MM-DD\"T\"HH24:MI:SS")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss", "YYYY-MM-DD\"T\"HH24:MI:SS")]
    public void ConstantFormatsTranslateInGroups(string format, string serverFormat)
    {
        using var context = CreateContext();
        // Literalize the test format so this exercises supported constant expressions,
        // rather than asking the translator to interpret a runtime format parameter.
        var selector = FormatSelector(format);
        var sql = context.Rows.Select(selector).GroupBy(text => text).Select(g => new { g.Key, Count = g.Count() }).ToQueryString();
        Assert.Contains("TO_CHAR(", sql, StringComparison.Ordinal);
        Assert.Contains(serverFormat, sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DateTimeConvertedToTicksDoesNotBecomeNumericText()
    {
        using var context = new ConvertedContext(new DbContextOptionsBuilder<ConvertedContext>()
            .UseDameng("Server=localhost;User=TEST;Password=unused").Options);
        var error = Assert.Throws<InvalidOperationException>(() => context.Rows.GroupBy(r => r.At.ToString())
            .Select(g => g.Count()).ToQueryString());
        Assert.Contains("ToString", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumToStringStillUsesTheExistingRelationalTranslator()
    {
        using var context = new EnumContext(new DbContextOptionsBuilder<EnumContext>()
            .UseDameng("Server=localhost;User=TEST;Password=unused").Options);
        var numberSql = context.Rows.Where(r => r.Status.ToString() == "Complete").ToQueryString();
        var textSql = context.Rows.Where(r => r.TextStatus.ToString() == "Complete").ToQueryString();
        var nullableSql = context.Rows.Where(r => r.OptionalStatus.ToString() == "Complete").ToQueryString();
        var nullableTextSql = context.Rows.Where(r => r.OptionalTextStatus.ToString() == "Complete").ToQueryString();
        Assert.Contains("CASE", numberSql, StringComparison.Ordinal);
        Assert.Contains("Complete", numberSql, StringComparison.Ordinal);
        Assert.Contains("TextStatus", textSql, StringComparison.Ordinal);
        Assert.Contains("CASE", nullableSql, StringComparison.Ordinal);
        Assert.Contains("OptionalTextStatus", nullableTextSql, StringComparison.Ordinal);
        var groupError = Assert.Throws<InvalidOperationException>(() => context.Rows
            .GroupBy(r => r.OptionalTextStatus.ToString()).Select(g => g.Count()).ToQueryString());
        Assert.Contains("LOB", groupError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedFormatsCultureAndRuntimeFormatsRemainExplicitTranslationBoundaries()
    {
        using var context = CreateContext();
        var unsupported = Assert.Throws<InvalidOperationException>(() => context.Rows.GroupBy(r => r.At!.Value.ToString("O"))
            .Select(g => g.Count()).ToQueryString());
        Assert.Contains("ToString", unsupported.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => r.At!.Value.ToString(CultureInfo.InvariantCulture) == "x").ToQueryString());
        var format = "yyyy-MM";
        Assert.Throws<InvalidOperationException>(() => context.Rows.GroupBy(r => r.At!.Value.ToString(format)).Select(g => g.Count()).ToQueryString());
    }
#pragma warning restore CA1305

    private static System.Linq.Expressions.Expression<Func<NumericAggregateRow, string>> FormatSelector(string format)
    {
        var row = System.Linq.Expressions.Expression.Parameter(typeof(NumericAggregateRow), "row");
        var value = System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Property(row, nameof(NumericAggregateRow.At)), "Value");
        var call = System.Linq.Expressions.Expression.Call(value, nameof(DateTime.ToString), Type.EmptyTypes,
            System.Linq.Expressions.Expression.Constant(format));
        return System.Linq.Expressions.Expression.Lambda<Func<NumericAggregateRow, string>>(call, row);
    }

    private enum EnumStatus { Pending, Complete }

    private sealed class EnumRow
    {
        public int Id { get; set; }
        public EnumStatus Status { get; set; }
        public EnumStatus TextStatus { get; set; }
        public EnumStatus? OptionalStatus { get; set; }
        public EnumStatus? OptionalTextStatus { get; set; }
    }

    private sealed class EnumContext(DbContextOptions<EnumContext> options) : DbContext(options)
    {
        public DbSet<EnumRow> Rows => Set<EnumRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EnumRow>().Property(r => r.TextStatus).HasConversion<string>().HasMaxLength(16);
            modelBuilder.Entity<EnumRow>().Property(r => r.OptionalTextStatus).HasConversion<string>().HasMaxLength(16);
        }
    }

    private sealed class ConvertedRow
    {
        public int Id { get; set; }
        public DateTime At { get; set; }
    }

    private sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : DbContext(options)
    {
        public DbSet<ConvertedRow> Rows => Set<ConvertedRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ConvertedRow>().Property(r => r.At).HasConversion<long>();
    }

    private static NumericAggregateContext CreateContext() => new(new DbContextOptionsBuilder<NumericAggregateContext>()
        .UseDameng("Server=localhost;User=TEST;Password=unused").Options);
}
