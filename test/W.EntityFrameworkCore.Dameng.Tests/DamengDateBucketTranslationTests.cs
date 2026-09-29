using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDateBucketTranslationTests
{
    [Fact]
    public void DateBucketMethodsAreServerOnly()
    {
        var value = new DateTime(2024, 2, 29);
        DateTime? nullableValue = null;

        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengTruncateHour(value));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengTruncateMinute(value));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengStartOfIsoWeek(value));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengQuarter(value));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengTruncateHour(nullableValue));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengTruncateMinute(nullableValue));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengStartOfIsoWeek(nullableValue));
        Assert.Throws<InvalidOperationException>(() => EF.Functions.DamengQuarter(nullableValue));
    }

    [Fact]
    public void BothDateTimeOverloadsTranslateFromMappedColumns()
    {
        using var context = CreateContext();

        var sql = context.Entities.Select(entity => new
        {
            Hour = EF.Functions.DamengTruncateHour(entity.RequiredAt),
            Minute = EF.Functions.DamengTruncateMinute(entity.RequiredAt),
            Week = EF.Functions.DamengStartOfIsoWeek(entity.RequiredAt),
            Quarter = EF.Functions.DamengQuarter(entity.RequiredAt),
            NullableHour = EF.Functions.DamengTruncateHour(entity.NullableAt),
            NullableMinute = EF.Functions.DamengTruncateMinute(entity.NullableAt),
            NullableWeek = EF.Functions.DamengStartOfIsoWeek(entity.NullableAt),
            NullableQuarter = EF.Functions.DamengQuarter(entity.NullableAt)
        }).ToQueryString();

        Assert.Equal(2, Count(sql, "'HH24'"));
        Assert.Equal(2, Count(sql, "'MI'"));
        Assert.Equal(2, Count(sql, "'IW'"));
        Assert.Equal(2, Count(sql, "DATEPART(quarter"));
        Assert.Contains("\"REQUIRED_AT\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"NULLABLE_AT\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DateBucketsTranslateInFilterGroupAndOrder()
    {
        using var context = CreateContext();
        var start = new DateTime(2024, 1, 1);

        var filteredSql = context.Entities
            .Where(entity => EF.Functions.DamengTruncateHour(entity.NullableAt) == start)
            .ToQueryString();
        var groupedSql = context.Entities
            .GroupBy(entity => EF.Functions.DamengQuarter(entity.NullableAt))
            .Select(group => new { Quarter = group.Key, Count = group.Count() })
            .ToQueryString();
        var orderedSql = context.Entities
            .OrderBy(entity => EF.Functions.DamengStartOfIsoWeek(entity.RequiredAt))
            .Select(entity => entity.Id)
            .ToQueryString();

        Assert.Contains("TRUNC(", filteredSql, StringComparison.Ordinal);
        Assert.Contains("'HH24'", filteredSql, StringComparison.Ordinal);
        Assert.Contains("DATEPART(quarter", groupedSql, StringComparison.Ordinal);
        Assert.Contains(" AS \"Key\"", groupedSql, StringComparison.Ordinal);
        Assert.Matches(@"GROUP BY ""[^""]+""\.""Key""", groupedSql);
        Assert.Contains("ORDER BY TRUNC(", orderedSql, StringComparison.Ordinal);
        Assert.Contains("'IW'", orderedSql, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
        => text.Split(part, StringSplitOptions.None).Length - 1;

    private static BucketContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BucketContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        return new BucketContext(options);
    }

    private sealed class BucketContext(DbContextOptions<BucketContext> options) : DbContext(options)
    {
        public DbSet<BucketEntity> Entities => Set<BucketEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<BucketEntity>(entity =>
            {
                entity.ToTable("DATE_BUCKET_CASES");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.RequiredAt).HasColumnName("REQUIRED_AT").HasColumnType("TIMESTAMP(7)");
                entity.Property(item => item.NullableAt).HasColumnName("NULLABLE_AT").HasColumnType("TIMESTAMP(7)");
            });
    }

    private sealed class BucketEntity
    {
        public long Id { get; set; }
        public DateTime RequiredAt { get; set; }
        public DateTime? NullableAt { get; set; }
    }
}
