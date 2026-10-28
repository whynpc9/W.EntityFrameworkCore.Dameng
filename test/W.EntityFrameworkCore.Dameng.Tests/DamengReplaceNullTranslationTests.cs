using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengReplaceNullTranslationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullReplacementDoesNotMakeNullPredicateTrue(bool relationalNulls)
    {
        using var context = Create(relationalNulls);
        string? replacement = null;
        var empty = context.Rows.Where(_ => false).ToQueryString();
        var all = context.Rows.Where(_ => true).ToQueryString();
        Assert.Equal(empty, context.Rows.Where(r => r.Name.Replace("a", null) == null).ToQueryString());
        Assert.Equal(all, context.Rows.Where(r => r.Name.Replace("a", null) != null).ToQueryString());
        Assert.Equal(empty, context.Rows.Where(r => r.Name.Replace("a", replacement) == null).ToQueryString());
        Assert.Equal(all, context.Rows.Where(r => r.Name.Replace("a", replacement) != null).ToQueryString());
        Assert.Equal(empty, context.Rows.Where(r => r.LargeText.Replace("a", replacement) == null).ToQueryString());
        Assert.Equal(all, context.Rows.Where(r => r.LargeText.Replace("a", replacement) != null).ToQueryString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableReplacementKeepsNullCompensationCorrect(bool relationalNulls)
    {
        using var context = Create(relationalNulls);
        Assert.Equal(context.Rows.Where(_ => false).ToQueryString(),
            context.Rows.Where(r => r.Name.Replace("a", r.Replacement) == null).ToQueryString());
        Assert.Equal(context.Rows.Where(_ => true).ToQueryString(),
            context.Rows.Where(r => r.Name.Replace("a", r.Replacement) != null).ToQueryString());
        foreach (var sql in new[] {
            context.Rows.Where(r => r.Name.Replace("a", r.Replacement) == "bc").ToQueryString(),
            context.Rows.Where(r => !(r.Name.Replace("a", r.Replacement) == "bc")).ToQueryString() })
        {
            Assert.Contains("COALESCE(", sql, StringComparison.Ordinal);
            Assert.Contains("REPLACE(", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReplacePreservesBoundedAndLobMappings()
    {
        using var context = Create(false);
        var bounded = context.Rows.Select(r => r.Name.Replace("a", r.Replacement)).ToQueryString();
        var lob = context.Rows.Select(r => r.LargeText.Replace("a", r.Replacement)).ToQueryString();
        Assert.Contains("REPLACE(", bounded, StringComparison.Ordinal);
        Assert.Contains("REPLACE(", lob, StringComparison.Ordinal);
        Assert.DoesNotContain(" AS NVARCHAR", lob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS VARCHAR", lob, StringComparison.OrdinalIgnoreCase);
    }

    private static Context Create(bool relationalNulls)
        => new(new DbContextOptionsBuilder<Context>().UseDameng("Server=localhost;User=test;Password=test",
            o => { if (relationalNulls) o.UseRelationalNulls(); }).Options);

    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>(e => {
                e.ToTable("REPLACE_CASES");
                e.Property(r => r.Name).HasMaxLength(200).IsRequired();
                e.Property(r => r.LargeText).HasColumnType("NCLOB").IsRequired();
                e.Property(r => r.Replacement).HasMaxLength(200);
            });
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string LargeText { get; set; } = "";
        public string? Replacement { get; set; }
    }
}
