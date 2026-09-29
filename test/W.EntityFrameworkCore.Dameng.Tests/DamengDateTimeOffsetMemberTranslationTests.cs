using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDateTimeOffsetMemberTranslationTests
{
    private static readonly string[] DateParts = ["year", "month", "day", "hour", "minute", "second"];

    [Fact]
    public void SixLocalDatePartsTranslateForRequiredAndNullableColumns()
    {
        using var context = CreateContext();

        var sql = context.Entities.Select(item => new
        {
            RequiredYear = item.RequiredAt.Year,
            RequiredMonth = item.RequiredAt.Month,
            RequiredDay = item.RequiredAt.Day,
            RequiredHour = item.RequiredAt.Hour,
            RequiredMinute = item.RequiredAt.Minute,
            RequiredSecond = item.RequiredAt.Second,
            NullableYear = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Year,
            NullableMonth = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Month,
            NullableDay = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Day,
            NullableHour = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Hour,
            NullableMinute = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Minute,
            NullableSecond = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Second
        }).ToQueryString();

        foreach (var part in DateParts)
        {
            Assert.Contains($"DATEPART({part},", sql, StringComparison.Ordinal);
        }

        Assert.Contains("\"REQUIRED_AT\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"NULLABLE_AT\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SYS_EXTRACT_UTC", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SixLocalDatePartsTranslateInServerFilters()
    {
        using var context = CreateContext();

        var sql = context.Entities.Where(item =>
            item.RequiredAt.Year == 2025
            && item.RequiredAt.Month == 1
            && item.RequiredAt.Day == 1
            && item.NullableAt != null
            && item.NullableAt.Value.Hour == 1
            && item.NullableAt.Value.Minute == 30
            && item.NullableAt.Value.Second == 0).ToQueryString();

        foreach (var part in DateParts)
        {
            Assert.Contains($"DATEPART({part},", sql, StringComparison.Ordinal);
        }

        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    private static OffsetContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OffsetContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        return new OffsetContext(options);
    }

    private sealed class OffsetContext(DbContextOptions<OffsetContext> options) : DbContext(options)
    {
        public DbSet<OffsetEntity> Entities => Set<OffsetEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OffsetEntity>(entity =>
            {
                entity.ToTable("OFFSET_CASES");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.RequiredAt).HasColumnName("REQUIRED_AT")
                    .HasColumnType("DATETIME(7) WITH TIME ZONE");
                entity.Property(item => item.NullableAt).HasColumnName("NULLABLE_AT")
                    .HasColumnType("DATETIME(7) WITH TIME ZONE");
            });
    }

    private sealed class OffsetEntity
    {
        public long Id { get; set; }
        public DateTimeOffset RequiredAt { get; set; }
        public DateTimeOffset? NullableAt { get; set; }
    }
}
