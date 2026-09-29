using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengStringAggregateTranslationTests
{
    [Fact]
    public void JoinPreservesOrderingFilterAndParameterizedSeparator()
    {
        using var context = CreateContext();
        var separator = "雪🌙";
        var sql = context.Rows
            .GroupBy(row => row.GroupId)
            .Select(group => new
            {
                group.Key,
                Joined = string.Join(separator, group
                    .Where(row => row.Ordinal > 0)
                    .OrderByDescending(row => row.Ordinal)
                    .Select(row => row.Text))
            })
            .ToQueryString();

        Assert.Contains("LISTAGG(", sql, StringComparison.Ordinal);
        Assert.Contains("WITHIN GROUP (ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("DESC", sql, StringComparison.Ordinal);
        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Contains("SUBSTR(", sql, StringComparison.Ordinal);
        Assert.Contains("LENGTH(", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(", sql, StringComparison.Ordinal);
        Assert.Contains(" || ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcatAndUnorderedJoinUseServerAggregate()
    {
        using var context = CreateContext();
        var sql = context.Rows
            .GroupBy(row => row.GroupId)
            .Select(group => new
            {
                group.Key,
                Joined = string.Join("|", group.Select(row => row.Text)),
                Concatenated = string.Concat(group.Select(row => row.Text))
            })
            .ToQueryString();

        Assert.Equal(2, Count(sql, "LISTAGG("));
        Assert.DoesNotContain("WITHIN GROUP", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctAndLobSelectorsFailTranslationExplicitly()
    {
        using var context = CreateContext();
        var distinct = Record.Exception(() => context.Rows
            .GroupBy(row => row.GroupId)
            .Select(group => string.Join("|", group.Select(row => row.Text).Distinct()))
            .ToQueryString());
        Assert.NotNull(distinct);
        Assert.Contains("Distinct", distinct.ToString(), StringComparison.Ordinal);

        var lob = Record.Exception(() => context.Rows
            .GroupBy(row => row.GroupId)
            .Select(group => string.Concat(group.Select(row => row.LobText)))
            .ToQueryString());
        Assert.NotNull(lob);
        Assert.Contains("bounded varying text selector", lob.ToString(), StringComparison.Ordinal);

        var fixedChar = Record.Exception(() => context.Rows
            .GroupBy(row => row.GroupId)
            .Select(group => string.Join("|", group.Select(row => row.FixedText)))
            .ToQueryString());
        Assert.NotNull(fixedChar);
        Assert.Contains("fixed CHAR", fixedChar.ToString(), StringComparison.Ordinal);

        var lobOrdering = Record.Exception(() => context.Rows
            .GroupBy(row => row.GroupId)
            .Select(group => string.Join("|", group
                .OrderBy(row => row.LobText)
                .Select(row => row.Text)))
            .ToQueryString());
        Assert.NotNull(lobOrdering);
        Assert.Contains("order by a LOB", lobOrdering.ToString(), StringComparison.Ordinal);
    }

    private static AggregateContext CreateContext()
        => new(new DbContextOptionsBuilder<AggregateContext>()
            .UseDameng("Server=localhost;User=TEST;Password=unused")
            .Options);

    private static int Count(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    private sealed class AggregateContext(DbContextOptions<AggregateContext> options) : DbContext(options)
    {
        public DbSet<AggregateRow> Rows => Set<AggregateRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AggregateRow>(entity =>
            {
                entity.ToTable("AGGREGATE_ROWS");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("ID");
                entity.Property(row => row.GroupId).HasColumnName("GROUP_ID");
                entity.Property(row => row.Ordinal).HasColumnName("ORDINAL");
                entity.Property(row => row.Text).HasColumnName("TEXT_VALUE").HasMaxLength(100);
                entity.Property(row => row.LobText).HasColumnName("LOB_VALUE").HasColumnType("nclob");
                entity.Property(row => row.FixedText).HasColumnName("FIXED_TEXT").HasColumnType("CHAR(4)");
            });
    }

    private sealed class AggregateRow
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int Ordinal { get; set; }
        public string? Text { get; set; }
        public string? LobText { get; set; }
        public string? FixedText { get; set; }
    }
}
