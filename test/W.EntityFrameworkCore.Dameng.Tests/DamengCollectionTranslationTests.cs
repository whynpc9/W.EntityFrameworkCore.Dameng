using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengCollectionTranslationTests
{
    private const string SourceSql =
        "SELECT CAST(0 AS INT) AS \"ID\" FROM dual "
        + "UNION ALL SELECT CAST(1 AS INT) AS \"ID\" FROM dual "
        + "UNION ALL SELECT CAST(NULL AS INT) AS \"ID\" FROM dual";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(998)]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(2000)]
    public void ParameterCollectionGeneratesDamengSql(int size)
    {
        using var context = CreateContext();
        int?[] values = Enumerable.Range(0, size).Select(index => (int?)index).ToArray();
        var sql = Source(context)
            .Where(row => values.Contains(row.Id))
            .ToQueryString();

        Assert.Contains("\"ID\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        if (size == 0)
        {
            Assert.Contains("0 = 1", sql, StringComparison.Ordinal);
        }
        else if (size == 1)
        {
            Assert.Contains(" = :", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(" IN (:", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ConstantCollectionGeneratesServerPredicate()
    {
        using var context = CreateContext();
        var sql = Source(context)
            .Where(row => new int?[] { 0, 1 }.Contains(row.Id))
            .ToQueryString();

        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("\"ID\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableContainsAndNegationGenerateNullCompensation()
    {
        using var context = CreateContext();
        int?[] values = [null, 1];
        var positive = Source(context)
            .Where(row => values.Contains(row.Id))
            .ToQueryString();
        var negative = Source(context)
            .Where(row => !values.Contains(row.Id))
            .ToQueryString();

        Assert.Contains("IS NULL", positive, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", negative, StringComparison.Ordinal);
        Assert.DoesNotContain("@", positive, StringComparison.Ordinal);
        Assert.DoesNotContain("@", negative, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoCollectionsFilterAndPaginationGenerateOneComposedQuery()
    {
        using var context = CreateContext();
        int?[] first = [0, 2];
        int?[] second = [1, 3];
        int? filter = 1;
        var sql = Source(context)
            .Where(row => (first.Contains(row.Id) || second.Contains(row.Id)) && row.Id == filter)
            .OrderBy(row => row.Id)
            .Skip(1)
            .Take(1)
            .ToQueryString();

        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    private static CollectionContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CollectionContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        return new CollectionContext(options);
    }

    private static IQueryable<CollectionRow> Source(CollectionContext context)
        => context.Rows.FromSqlRaw(SourceSql);

    private sealed class CollectionContext(DbContextOptions<CollectionContext> options)
        : DbContext(options)
    {
        public DbSet<CollectionRow> Rows => Set<CollectionRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CollectionRow>(entity =>
            {
                entity.HasNoKey();
                entity.Property(row => row.Id).HasColumnName("ID");
            });
    }

    private sealed class CollectionRow
    {
        public int? Id { get; set; }
    }
}
