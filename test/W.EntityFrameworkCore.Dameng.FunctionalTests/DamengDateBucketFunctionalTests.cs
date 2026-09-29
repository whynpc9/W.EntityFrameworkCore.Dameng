using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengDateBucketFunctionalTests
{
    [DamengFact]
    public async Task DateBucketsRunOnServerInProjectionFilterGroupAndOrder()
    {
        var store = new BucketStore();
        try
        {
            await store.CreateTableAsync();
            var commands = new CommandCaptureInterceptor();
            await using var context = CreateContext(store, commands);
            var cases = QueryTranslationCases.DateBuckets;
            var rows = cases.Select(item => new BucketEntity
            {
                Id = item.Id,
                RequiredAt = item.Value,
                NullableAt = item.Value
            }).ToList();

            // Q2 and Q3 are included until the shared fixture covers all four quarters.
            rows.Add(new BucketEntity
            {
                Id = 5,
                RequiredAt = new DateTime(2024, 4, 1, 0, 0, 0).AddTicks(1),
                NullableAt = new DateTime(2024, 4, 1, 0, 0, 0).AddTicks(1)
            });
            rows.Add(new BucketEntity
            {
                Id = 6,
                RequiredAt = new DateTime(2024, 7, 1, 0, 0, 0).AddTicks(1),
                NullableAt = new DateTime(2024, 7, 1, 0, 0, 0).AddTicks(1)
            });
            rows.Add(new BucketEntity
            {
                Id = 7,
                RequiredAt = new DateTime(2024, 1, 1),
                NullableAt = null
            });
            context.Entities.AddRange(rows);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            commands.Commands.Clear();

            var projection = context.Entities
                .OrderBy(item => item.Id)
                .Select(item => new BucketProjection
                {
                    Id = item.Id,
                    Hour = EF.Functions.DamengTruncateHour(item.NullableAt),
                    Minute = EF.Functions.DamengTruncateMinute(item.NullableAt),
                    Week = EF.Functions.DamengStartOfIsoWeek(item.NullableAt),
                    Quarter = EF.Functions.DamengQuarter(item.NullableAt)
                });
            var projectionSql = projection.ToQueryString();
            Assert.Contains("TRUNC(", projectionSql, StringComparison.Ordinal);
            Assert.Contains("DATEPART(quarter", projectionSql, StringComparison.Ordinal);
            var projected = await projection.ToListAsync();
            AssertExecutedSql(commands, "TRUNC(", "DATEPART(quarter");
            foreach (var expected in cases)
            {
                var actual = Assert.Single(projected, item => item.Id == expected.Id);
                Assert.Equal(expected.ExpectedHour, actual.Hour);
                Assert.Equal(expected.ExpectedMinute, actual.Minute);
                Assert.Equal(expected.ExpectedIsoWeekStart, actual.Week);
                Assert.Equal(expected.ExpectedQuarter, actual.Quarter);
            }

            Assert.Null(Assert.Single(projected, item => item.Id == 7).Hour);
            Assert.Null(Assert.Single(projected, item => item.Id == 7).Minute);
            Assert.Null(Assert.Single(projected, item => item.Id == 7).Week);
            Assert.Null(Assert.Single(projected, item => item.Id == 7).Quarter);
            Assert.Equal(2, Assert.Single(projected, item => item.Id == 5).Quarter);
            Assert.Equal(3, Assert.Single(projected, item => item.Id == 6).Quarter);

            commands.Commands.Clear();
            var required = await context.Entities
                .Where(item => item.Id == 1)
                .Select(item => new
                {
                    Hour = EF.Functions.DamengTruncateHour(item.RequiredAt),
                    Minute = EF.Functions.DamengTruncateMinute(item.RequiredAt),
                    Week = EF.Functions.DamengStartOfIsoWeek(item.RequiredAt),
                    Quarter = EF.Functions.DamengQuarter(item.RequiredAt)
                })
                .SingleAsync();
            AssertExecutedSql(commands, "TRUNC(", "DATEPART(quarter");
            Assert.Equal(cases[0].ExpectedHour, required.Hour);
            Assert.Equal(cases[0].ExpectedMinute, required.Minute);
            Assert.Equal(cases[0].ExpectedIsoWeekStart, required.Week);
            Assert.Equal(cases[0].ExpectedQuarter, required.Quarter);

            commands.Commands.Clear();
            var filteredIds = await context.Entities
                .Where(item => EF.Functions.DamengTruncateHour(item.NullableAt) == cases[0].ExpectedHour)
                .Select(item => item.Id)
                .ToListAsync();
            AssertExecutedSql(commands, "TRUNC(", "'HH24'");
            Assert.Equal([1L], filteredIds);

            commands.Commands.Clear();
            var groups = await context.Entities
                .GroupBy(item => EF.Functions.DamengQuarter(item.NullableAt))
                .Select(group => new QuarterGroup { Quarter = group.Key, Count = group.Count() })
                .ToListAsync();
            var groupedSql = AssertExecutedSql(commands, "DATEPART(quarter", " AS \"Key\"", "GROUP BY ");
            Assert.Matches(@"GROUP BY ""[^""]+""\.""Key""", groupedSql);
            Assert.Equal(3, Assert.Single(groups, group => group.Quarter == 1).Count);
            Assert.Equal(1, Assert.Single(groups, group => group.Quarter == 2).Count);
            Assert.Equal(1, Assert.Single(groups, group => group.Quarter == 3).Count);
            Assert.Equal(1, Assert.Single(groups, group => group.Quarter == 4).Count);
            Assert.Equal(1, Assert.Single(groups, group => group.Quarter is null).Count);

            commands.Commands.Clear();
            var orderedIds = await context.Entities
                .Where(item => item.NullableAt != null)
                .OrderBy(item => EF.Functions.DamengStartOfIsoWeek(item.NullableAt))
                .ThenBy(item => item.Id)
                .Select(item => item.Id)
                .ToListAsync();
            AssertExecutedSql(commands, "ORDER BY TRUNC(", "'IW'");
            Assert.Equal([1L, 2L, 4L, 3L, 5L, 6L], orderedIds);
        }
        finally
        {
            await store.DropTableAsync();
        }
    }

    private static string AssertExecutedSql(CommandCaptureInterceptor capture, params string[] fragments)
    {
        var sql = Assert.Single(capture.Commands);
        foreach (var fragment in fragments)
        {
            Assert.Contains(fragment, sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
        return sql;
    }

    private static BucketContext CreateContext(BucketStore store, CommandCaptureInterceptor commands)
    {
        var options = new DbContextOptionsBuilder<BucketContext>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, BucketModelCacheKeyFactory>()
            .AddInterceptors(commands)
            .Options;
        return new BucketContext(options, store.TableName);
    }

    private sealed class BucketProjection
    {
        public long Id { get; set; }
        public DateTime? Hour { get; set; }
        public DateTime? Minute { get; set; }
        public DateTime? Week { get; set; }
        public int? Quarter { get; set; }
    }

    private sealed class QuarterGroup
    {
        public int? Quarter { get; set; }
        public int Count { get; set; }
    }

    private sealed class BucketEntity
    {
        public long Id { get; set; }
        public DateTime RequiredAt { get; set; }
        public DateTime? NullableAt { get; set; }
    }

    private sealed class BucketContext(DbContextOptions<BucketContext> options, string tableName) : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<BucketEntity> Entities => Set<BucketEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<BucketEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
                entity.Property(item => item.RequiredAt).HasColumnName("REQUIRED_AT").HasColumnType("TIMESTAMP(7)");
                entity.Property(item => item.NullableAt).HasColumnName("NULLABLE_AT").HasColumnType("TIMESTAMP(7)");
            });
    }

    private sealed class BucketModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is BucketContext bucketContext
                ? (context.GetType(), bucketContext.TableName, designTime)
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

    private sealed class BucketStore
    {
        private bool _tableCreated;

        public BucketStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
            TableName = $"EF10_DB_{suffix}";
            PrimaryKeyName = $"PK_DB_{suffix}";
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
                    "REQUIRED_AT" TIMESTAMP(7) NOT NULL,
                    "NULLABLE_AT" TIMESTAMP(7) NULL,
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
