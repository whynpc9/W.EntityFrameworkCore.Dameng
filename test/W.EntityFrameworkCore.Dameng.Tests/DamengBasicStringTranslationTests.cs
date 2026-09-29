using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengBasicStringTranslationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IsNullOrEmptyIsTranslatedInFilterAndProjection(
        bool lob,
        bool useRelationalNulls)
    {
        using var context = CreateContext(useRelationalNulls);

        var filteredSql = lob
            ? context.Entities.Where(entity => string.IsNullOrEmpty(entity.LargeText))
                .Select(entity => entity.Id).ToQueryString()
            : context.Entities.Where(entity => string.IsNullOrEmpty(entity.ShortText))
                .Select(entity => entity.Id).ToQueryString();
        var projectedSql = lob
            ? context.Entities.Select(entity => new
            {
                entity.Id,
                IsEmpty = string.IsNullOrEmpty(entity.LargeText)
            }).ToQueryString()
            : context.Entities.Select(entity => new
            {
                entity.Id,
                IsEmpty = string.IsNullOrEmpty(entity.ShortText)
            }).ToQueryString();

        var column = lob ? "\"LARGE_TEXT\"" : "\"SHORT_TEXT\"";
        Assert.Contains(column, filteredSql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", filteredSql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(", filteredSql, StringComparison.Ordinal);
        Assert.Contains(column, projectedSql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", projectedSql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(", projectedSql, StringComparison.Ordinal);
        Assert.Contains("CASE", projectedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TEXT_EQUAL(", filteredSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TEXT_EQUAL(", projectedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", filteredSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", projectedSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, "Concat2")]
    [InlineData(false, false, "Concat3")]
    [InlineData(false, false, "Concat4")]
    [InlineData(false, false, "Add")]
    [InlineData(false, true, "Concat2")]
    [InlineData(false, true, "Concat3")]
    [InlineData(false, true, "Concat4")]
    [InlineData(false, true, "Add")]
    [InlineData(true, false, "Concat2")]
    [InlineData(true, false, "Concat3")]
    [InlineData(true, false, "Concat4")]
    [InlineData(true, false, "Add")]
    [InlineData(true, true, "Concat2")]
    [InlineData(true, true, "Concat3")]
    [InlineData(true, true, "Concat4")]
    [InlineData(true, true, "Add")]
    public void FixedStringConcatenationRunsInServerFilterAndProjection(
        bool lob,
        bool useRelationalNulls,
        string operation)
    {
        using var context = CreateContext(useRelationalNulls);

        var projectedSql = Project(context, lob, operation).ToQueryString();
        var filteredSql = Filter(context, lob, operation).ToQueryString();

        var column = lob ? "\"LARGE_TEXT\"" : "\"SHORT_TEXT\"";
        Assert.Contains(column, projectedSql, StringComparison.Ordinal);
        Assert.Contains(" || ", projectedSql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(", projectedSql, StringComparison.Ordinal);
        Assert.Contains(column, filteredSql, StringComparison.Ordinal);
        Assert.Contains(" || ", filteredSql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(", filteredSql, StringComparison.Ordinal);
        if (lob)
        {
            Assert.Contains("CAST('' AS NCLOB)", projectedSql, StringComparison.Ordinal);
            Assert.Contains("CAST('' AS NCLOB)", filteredSql, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(" AS VARCHAR", projectedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS VARCHAR", filteredSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS NVARCHAR", projectedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS NVARCHAR", filteredSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", projectedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", filteredSql, StringComparison.Ordinal);
    }

    private static IQueryable<string?> Project(
        StringContext context,
        bool lob,
        string operation)
        => (lob, operation) switch
        {
            (false, "Concat2") => context.Entities.Select(entity =>
                string.Concat(entity.ShortText, entity.OtherShortText)),
            (false, "Concat3") => context.Entities.Select(entity =>
                string.Concat(entity.ShortText, entity.OtherShortText, entity.ThirdShortText)),
            (false, "Concat4") => context.Entities.Select(entity =>
                string.Concat(entity.ShortText, entity.OtherShortText,
                    entity.ThirdShortText, entity.FourthShortText)),
            (false, "Add") => context.Entities.Select(entity =>
                entity.ShortText + entity.OtherShortText),
            (true, "Concat2") => context.Entities.Select(entity =>
                string.Concat(entity.LargeText, entity.OtherLargeText)),
            (true, "Concat3") => context.Entities.Select(entity =>
                string.Concat(entity.LargeText, entity.OtherLargeText, entity.ThirdLargeText)),
            (true, "Concat4") => context.Entities.Select(entity =>
                string.Concat(entity.LargeText, entity.OtherLargeText,
                    entity.ThirdLargeText, entity.FourthLargeText)),
            (true, "Add") => context.Entities.Select(entity =>
                entity.LargeText + entity.OtherLargeText),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static IQueryable<long> Filter(
        StringContext context,
        bool lob,
        string operation)
        => (lob, operation) switch
        {
            (false, "Concat2") => context.Entities.Where(entity =>
                string.Concat(entity.ShortText, entity.OtherShortText) == "雪🌙雨")
                .Select(entity => entity.Id),
            (false, "Concat3") => context.Entities.Where(entity =>
                string.Concat(entity.ShortText, entity.OtherShortText,
                    entity.ThirdShortText) == "雪🌙雨林")
                .Select(entity => entity.Id),
            (false, "Concat4") => context.Entities.Where(entity =>
                string.Concat(entity.ShortText, entity.OtherShortText,
                    entity.ThirdShortText, entity.FourthShortText) == "雪🌙雨林🙂")
                .Select(entity => entity.Id),
            (false, "Add") => context.Entities.Where(entity =>
                entity.ShortText + entity.OtherShortText == "雪🌙雨")
                .Select(entity => entity.Id),
            (true, "Concat2") => context.Entities.Where(entity =>
                string.Concat(entity.LargeText, entity.OtherLargeText) == "雪🌙雨")
                .Select(entity => entity.Id),
            (true, "Concat3") => context.Entities.Where(entity =>
                string.Concat(entity.LargeText, entity.OtherLargeText,
                    entity.ThirdLargeText) == "雪🌙雨林")
                .Select(entity => entity.Id),
            (true, "Concat4") => context.Entities.Where(entity =>
                string.Concat(entity.LargeText, entity.OtherLargeText,
                    entity.ThirdLargeText, entity.FourthLargeText) == "雪🌙雨林🙂")
                .Select(entity => entity.Id),
            (true, "Add") => context.Entities.Where(entity =>
                entity.LargeText + entity.OtherLargeText == "雪🌙雨")
                .Select(entity => entity.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static StringContext CreateContext(bool useRelationalNulls)
    {
        var options = new DbContextOptionsBuilder<StringContext>()
            .UseDameng(
                "Server=localhost;Port=5236;User=test;Password=test",
                damengOptions =>
                {
                    if (useRelationalNulls)
                    {
                        damengOptions.UseRelationalNulls();
                    }
                })
            .Options;
        return new StringContext(options);
    }

    private sealed class StringContext(DbContextOptions<StringContext> options) : DbContext(options)
    {
        public DbSet<StringEntity> Entities => Set<StringEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<StringEntity>(entity =>
            {
                entity.ToTable("STRING_CASES");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.ShortText).HasColumnName("SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.OtherShortText).HasColumnName("OTHER_SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.ThirdShortText).HasColumnName("THIRD_SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.FourthShortText).HasColumnName("FOURTH_SHORT_TEXT").HasMaxLength(200);
                entity.Property(item => item.LargeText).HasColumnName("LARGE_TEXT");
                entity.Property(item => item.OtherLargeText).HasColumnName("OTHER_LARGE_TEXT");
                entity.Property(item => item.ThirdLargeText).HasColumnName("THIRD_LARGE_TEXT");
                entity.Property(item => item.FourthLargeText).HasColumnName("FOURTH_LARGE_TEXT");
            });
    }

    private sealed class StringEntity
    {
        public long Id { get; set; }
        public string? ShortText { get; set; }
        public string? OtherShortText { get; set; }
        public string? ThirdShortText { get; set; }
        public string? FourthShortText { get; set; }
        public string? LargeText { get; set; }
        public string? OtherLargeText { get; set; }
        public string? ThirdLargeText { get; set; }
        public string? FourthLargeText { get; set; }
    }
}
