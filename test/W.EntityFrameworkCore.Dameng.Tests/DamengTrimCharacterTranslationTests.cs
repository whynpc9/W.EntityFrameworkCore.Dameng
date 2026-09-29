using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

#pragma warning disable CA1861 // Inline arrays are intentional SQL constants, not captured query parameters.
public sealed class DamengTrimCharacterTranslationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleCharactersTranslateOnBothEndsAndPreserveLobMapping(bool lob)
    {
        using var context = CreateContext();

        var both = lob
            ? context.Entities.Where(entity => entity.LargeText!.Trim('*') == "正文")
                .Select(entity => entity.LargeText!.Trim('*')).ToQueryString()
            : context.Entities.Where(entity => entity.ShortText!.Trim('*') == "正文")
                .Select(entity => entity.ShortText!.Trim('*')).ToQueryString();
        var start = lob
            ? context.Entities.Where(entity => entity.LargeText!.TrimStart('雪') == "文字")
                .Select(entity => entity.LargeText!.TrimStart('雪')).ToQueryString()
            : context.Entities.Where(entity => entity.ShortText!.TrimStart('雪') == "文字")
                .Select(entity => entity.ShortText!.TrimStart('雪')).ToQueryString();
        var end = lob
            ? context.Entities.Where(entity => entity.LargeText!.TrimEnd('\'') == "正文")
                .Select(entity => entity.LargeText!.TrimEnd('\'')).ToQueryString()
            : context.Entities.Where(entity => entity.ShortText!.TrimEnd('\'') == "正文")
                .Select(entity => entity.ShortText!.TrimEnd('\'')).ToQueryString();

        Assert.Contains("RTRIM(LTRIM(", both, StringComparison.Ordinal);
        Assert.Contains("LTRIM(", start, StringComparison.Ordinal);
        Assert.DoesNotContain("RTRIM(", start, StringComparison.Ordinal);
        Assert.Contains("RTRIM(", end, StringComparison.Ordinal);
        Assert.DoesNotContain("LTRIM(", end, StringComparison.Ordinal);
        Assert.Contains("''", end, StringComparison.Ordinal);
        Assert.DoesNotContain(" AS VARCHAR", both, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS NVARCHAR", both, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantCharacterSetsTranslateAsSetsForAllThreeMethods(bool lob)
    {
        using var context = CreateContext();

        var both = lob
            ? context.Entities.Where(entity => entity.LargeText!.Trim(new[] { '*', '.' }) == "正文")
                .Select(entity => entity.LargeText!.Trim(new[] { '*', '.' })).ToQueryString()
            : context.Entities.Where(entity => entity.ShortText!.Trim(new[] { '*', '.' }) == "正文")
                .Select(entity => entity.ShortText!.Trim(new[] { '*', '.' })).ToQueryString();
        var start = lob
            ? context.Entities.Where(entity => entity.LargeText!.TrimStart(new[] { '雪', '雨' }) == "文字雨")
                .Select(entity => entity.LargeText!.TrimStart(new[] { '雪', '雨' })).ToQueryString()
            : context.Entities.Where(entity => entity.ShortText!.TrimStart(new[] { '雪', '雨' }) == "文字雨")
                .Select(entity => entity.ShortText!.TrimStart(new[] { '雪', '雨' })).ToQueryString();
        var end = lob
            ? context.Entities.Where(entity => entity.LargeText!.TrimEnd(new[] { '\t', '\'' }) == "正文")
                .Select(entity => entity.LargeText!.TrimEnd(new[] { '\t', '\'' })).ToQueryString()
            : context.Entities.Where(entity => entity.ShortText!.TrimEnd(new[] { '\t', '\'' }) == "正文")
                .Select(entity => entity.ShortText!.TrimEnd(new[] { '\t', '\'' })).ToQueryString();

        Assert.Contains("RTRIM(LTRIM(", both, StringComparison.Ordinal);
        Assert.Contains("LTRIM(", start, StringComparison.Ordinal);
        Assert.Contains("RTRIM(", end, StringComparison.Ordinal);
        Assert.Contains("''", end, StringComparison.Ordinal);
        Assert.DoesNotContain(" AS VARCHAR", both, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS NVARCHAR", both, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Captured")]
    [InlineData("Null")]
    [InlineData("Empty")]
    [InlineData("SurrogateChar")]
    [InlineData("SurrogateArray")]
    public void UnsupportedCharacterInputsDoNotTranslateInWhere(string variant)
    {
        using var context = CreateContext();
        var captured = new[] { '*', '.' };
        char[]? nullArray = null;
        char[] emptyArray = [];

        Func<string> toSql = variant switch
        {
            "Captured" => () => context.Entities
                .Where(entity => entity.ShortText!.Trim(captured) == "正文")
                .ToQueryString(),
            "Null" => () => context.Entities
                .Where(entity => entity.ShortText!.Trim(nullArray) == "正文")
                .ToQueryString(),
            "Empty" => () => context.Entities
                .Where(entity => entity.ShortText!.Trim(emptyArray) == "正文")
                .ToQueryString(),
            "SurrogateChar" => () => context.Entities
                .Where(entity => entity.ShortText!.Trim('\uD83D') == "正文")
                .ToQueryString(),
            "SurrogateArray" => () => context.Entities
                .Where(entity => entity.ShortText!.Trim(new[] { '\uD83D' }) == "正文")
                .ToQueryString(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };

        var exception = Assert.Throws<InvalidOperationException>(toSql);
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trim", exception.Message, StringComparison.Ordinal);
    }

    private static TrimContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrimContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        return new TrimContext(options);
    }

    private sealed class TrimContext(DbContextOptions<TrimContext> options) : DbContext(options)
    {
        public DbSet<TrimEntity> Entities => Set<TrimEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TrimEntity>(entity =>
            {
                entity.ToTable("TRIM_CASES");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.ShortText).HasMaxLength(200);
            });
    }

    private sealed class TrimEntity
    {
        public long Id { get; set; }
        public string? ShortText { get; set; }
        public string? LargeText { get; set; }
    }
}
#pragma warning restore CA1861
