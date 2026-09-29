using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengDateTimeOffsetMemberFunctionalTests
{
    private static readonly string[] DateParts = ["year", "month", "day", "hour", "minute", "second"];

    [DamengFact]
    public async Task LocalDatePartsRunOnServerAcrossOffsetsAndNulls()
    {
        var store = new OffsetStore();
        try
        {
            await store.CreateTableAsync();
            var capture = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, capture);
            var fixture = QueryTranslationCases.DateTimeOffsets;
            var rows = fixture.Select(item => new OffsetEntity
            {
                Id = item.Id,
                RequiredAt = item.Value,
                NullableAt = item.Value
            }).ToList();

            // 两组时间各自对应同一 UTC 瞬间，但本地日历日期不同。
            rows.Add(new OffsetEntity
            {
                Id = 6,
                RequiredAt = new DateTimeOffset(2025, 1, 1, 1, 30, 45, TimeSpan.FromHours(8)),
                NullableAt = new DateTimeOffset(2025, 1, 1, 1, 30, 45, TimeSpan.FromHours(8))
            });
            rows.Add(new OffsetEntity
            {
                Id = 7,
                RequiredAt = new DateTimeOffset(2024, 12, 31, 12, 30, 45, TimeSpan.FromHours(-5)),
                NullableAt = new DateTimeOffset(2024, 12, 31, 12, 30, 45, TimeSpan.FromHours(-5))
            });
            rows.Add(new OffsetEntity
            {
                Id = 8,
                RequiredAt = new DateTimeOffset(2024, 5, 1, 0, 30, 12, TimeSpan.FromMinutes(330)),
                NullableAt = new DateTimeOffset(2024, 5, 1, 0, 30, 12, TimeSpan.FromMinutes(330))
            });
            rows.Add(new OffsetEntity
            {
                Id = 9,
                RequiredAt = new DateTimeOffset(2024, 4, 30, 15, 0, 12, TimeSpan.FromHours(-4)),
                NullableAt = new DateTimeOffset(2024, 4, 30, 15, 0, 12, TimeSpan.FromHours(-4))
            });
            rows.Add(new OffsetEntity
            {
                Id = 10,
                RequiredAt = fixture[0].Value,
                NullableAt = null
            });
            Assert.Equal(rows[5].RequiredAt, rows[6].RequiredAt);
            Assert.Equal(rows[7].RequiredAt, rows[8].RequiredAt);

            context.Entities.AddRange(rows);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            capture.Commands.Clear();

            var query = context.Entities.OrderBy(item => item.Id).Select(item => new PartsProjection
            {
                Id = item.Id,
                Year = item.RequiredAt.Year,
                Month = item.RequiredAt.Month,
                Day = item.RequiredAt.Day,
                Hour = item.RequiredAt.Hour,
                Minute = item.RequiredAt.Minute,
                Second = item.RequiredAt.Second,
                NullableYear = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Year,
                NullableMonth = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Month,
                NullableDay = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Day,
                NullableHour = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Hour,
                NullableMinute = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Minute,
                NullableSecond = item.NullableAt == null ? (int?)null : item.NullableAt.Value.Second
            });
            AssertDatePartSql(query.ToQueryString());
            var projected = await query.ToListAsync();
            AssertDatePartSql(Assert.Single(capture.Commands));
            foreach (var item in rows)
            {
                var actual = Assert.Single(projected, result => result.Id == item.Id);
                Assert.Equal(item.RequiredAt.Year, actual.Year);
                Assert.Equal(item.RequiredAt.Month, actual.Month);
                Assert.Equal(item.RequiredAt.Day, actual.Day);
                Assert.Equal(item.RequiredAt.Hour, actual.Hour);
                Assert.Equal(item.RequiredAt.Minute, actual.Minute);
                Assert.Equal(item.RequiredAt.Second, actual.Second);
                Assert.Equal(item.NullableAt?.Year, actual.NullableYear);
                Assert.Equal(item.NullableAt?.Month, actual.NullableMonth);
                Assert.Equal(item.NullableAt?.Day, actual.NullableDay);
                Assert.Equal(item.NullableAt?.Hour, actual.NullableHour);
                Assert.Equal(item.NullableAt?.Minute, actual.NullableMinute);
                Assert.Equal(item.NullableAt?.Second, actual.NullableSecond);
            }

            // 相同瞬间必须保留各自存储的本地日期部件。
            Assert.Equal(2025, Assert.Single(projected, item => item.Id == 6).Year);
            Assert.Equal(2024, Assert.Single(projected, item => item.Id == 7).Year);
            Assert.Equal(5, Assert.Single(projected, item => item.Id == 8).Month);
            Assert.Equal(4, Assert.Single(projected, item => item.Id == 9).Month);

            capture.Commands.Clear();
            var requiredIds = await context.Entities
                .Where(item => item.RequiredAt.Year == 2025
                    && item.RequiredAt.Month == 1
                    && item.RequiredAt.Day == 1)
                .Select(item => item.Id)
                .ToListAsync();
            Assert.Equal([6L], requiredIds);
            AssertFilterSql(Assert.Single(capture.Commands), "year", "month", "day");

            capture.Commands.Clear();
            var nullableIds = await context.Entities
                .Where(item => item.NullableAt != null
                    && item.NullableAt.Value.Month == 4
                    && item.NullableAt.Value.Day == 30
                    && item.NullableAt.Value.Hour == 15
                    && item.NullableAt.Value.Minute == 0
                    && item.NullableAt.Value.Second == 12)
                .Select(item => item.Id)
                .ToListAsync();
            Assert.Equal([9L], nullableIds);
            AssertFilterSql(Assert.Single(capture.Commands), "month", "day", "hour", "minute", "second");
        }
        finally
        {
            await store.DropTableAsync();
        }
    }

    private static void AssertDatePartSql(string sql)
    {
        foreach (var part in DateParts)
        {
            Assert.Contains($"DATEPART({part},", sql, StringComparison.Ordinal);
        }

        Assert.Contains("\"REQUIRED_AT\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"NULLABLE_AT\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SYS_EXTRACT_UTC", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    private static void AssertFilterSql(string sql, params string[] parts)
    {
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        foreach (var part in parts)
        {
            Assert.Contains($"DATEPART({part},", sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    private static OffsetContext CreateContext(OffsetStore store, CommandCaptureInterceptor capture)
    {
        var options = new DbContextOptionsBuilder<OffsetContext>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, OffsetModelCacheKeyFactory>()
            .AddInterceptors(capture)
            .Options;
        return new OffsetContext(options, store.TableName);
    }

    private sealed class PartsProjection
    {
        public long Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public int Second { get; set; }
        public int? NullableYear { get; set; }
        public int? NullableMonth { get; set; }
        public int? NullableDay { get; set; }
        public int? NullableHour { get; set; }
        public int? NullableMinute { get; set; }
        public int? NullableSecond { get; set; }
    }

    private sealed class OffsetEntity
    {
        public long Id { get; set; }
        public DateTimeOffset RequiredAt { get; set; }
        public DateTimeOffset? NullableAt { get; set; }
    }

    private sealed class OffsetContext(DbContextOptions<OffsetContext> options, string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;
        public DbSet<OffsetEntity> Entities => Set<OffsetEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OffsetEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.RequiredAt).HasColumnName("REQUIRED_AT")
                    .HasColumnType("DATETIME(7) WITH TIME ZONE");
                entity.Property(item => item.NullableAt).HasColumnName("NULLABLE_AT")
                    .HasColumnType("DATETIME(7) WITH TIME ZONE");
            });
    }

    private sealed class OffsetModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is OffsetContext offsetContext
                ? (context.GetType(), offsetContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class OffsetStore
    {
        private bool _tableCreated;

        public OffsetStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_DT_{suffix}";
            PrimaryKeyName = $"PK_DT_{suffix}";
        }

        public string ConnectionString { get; }
        public string TableName { get; }
        public string PrimaryKeyName { get; }

        public async Task CreateTableAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE "{TableName}" (
                    "ID" BIGINT NOT NULL,
                    "REQUIRED_AT" DATETIME(7) WITH TIME ZONE NOT NULL,
                    "NULLABLE_AT" DATETIME(7) WITH TIME ZONE NULL,
                    CONSTRAINT "{PrimaryKeyName}" NOT CLUSTER PRIMARY KEY ("ID")
                )
                """;
            await command.ExecuteNonQueryAsync();
            _tableCreated = true;
        }

        public async Task DropTableAsync()
        {
            if (!_tableCreated)
            {
                return;
            }

            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE \"{TableName}\"";
            await command.ExecuteNonQueryAsync();
            _tableCreated = false;
        }
    }
}
