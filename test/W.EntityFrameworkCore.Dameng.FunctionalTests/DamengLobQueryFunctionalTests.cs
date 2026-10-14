using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengLobQueryFunctionalTests
{
    [DamengFact]
    public Task DefaultLobMappingsSupportParameterEqualityAndTextSearch()
        => LobQueryStore.WithTableAsync(
            async store =>
            {
                var commandLog = new List<string>();
                var expectedText = "达梦数据库 NCLOB 等值与包含查询";
                var expectedPayload = new byte[] { 0x00, 0x01, 0xA5, 0xFF };

                await using (var context = CreateContext(store, commandLog))
                {
                    context.Entities.AddRange(
                        new LobQueryEntity
                        {
                            Id = 1,
                            LargeText = expectedText,
                            Payload = expectedPayload
                        },
                        new LobQueryEntity
                        {
                            Id = 2,
                            LargeText = "另一条大字段记录",
                            Payload = [0x10, 0x20]
                        });
                    await context.SaveChangesAsync();
                }

                commandLog.Clear();

                await using (var context = CreateContext(store, commandLog))
                {
                    Assert.Equal(
                        1,
                        await context.Entities
                            .Where(entity => entity.LargeText == expectedText)
                            .Select(entity => entity.Id)
                            .SingleAsync());
                    Assert.Equal(
                        1,
                        await context.Entities
                            .Where(entity => entity.Payload == expectedPayload)
                            .Select(entity => entity.Id)
                            .SingleAsync());
                    Assert.Equal(
                        1,
                        await context.Entities
                            .Where(entity => entity.LargeText!.Contains("NCLOB 等值"))
                            .Select(entity => entity.Id)
                            .SingleAsync());
                }

                var executedSql = string.Join(Environment.NewLine, commandLog);
                Assert.Contains("TEXT_EQUAL(", executedSql, StringComparison.Ordinal);
                Assert.Contains("BLOB_EQUAL(", executedSql, StringComparison.Ordinal);
                Assert.Contains("INSTR(", executedSql, StringComparison.Ordinal);
            });

    private static LobQueryContext CreateContext(
        LobQueryStore store,
        ICollection<string> commandLog)
    {
        var options = new DbContextOptionsBuilder<LobQueryContext>()
            .UseDamengUnderTest(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, LobQueryModelCacheKeyFactory>()
            .LogTo(commandLog.Add)
            .EnableDetailedErrors()
            .Options;

        return new LobQueryContext(options, store.TableName);
    }

    private sealed class LobQueryContext(
        DbContextOptions<LobQueryContext> options,
        string tableName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<LobQueryEntity> Entities
            => Set<LobQueryEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<LobQueryEntity>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id);

                    entity.Property(item => item.Id)
                        .HasColumnName("ID")
                        .ValueGeneratedNever();
                    entity.Property(item => item.LargeText)
                        .HasColumnName("LARGE_TEXT");
                    entity.Property(item => item.Payload)
                        .HasColumnName("PAYLOAD");
                });
    }

    private sealed class LobQueryModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is LobQueryContext lobContext
                ? (context.GetType(), lobContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class LobQueryEntity
    {
        public long Id { get; set; }

        public string? LargeText { get; set; }

        public byte[]? Payload { get; set; }
    }

    private sealed class LobQueryStore
    {
        private bool _tableCreated;

        private LobQueryStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid()
                .ToString("N", CultureInfo.InvariantCulture)[..12]
                .ToUpperInvariant();
            TableName = $"EF10_LQ_{suffix}";
            PrimaryKeyName = $"PK_LQ_{suffix}";
        }

        public string ConnectionString { get; }

        public string TableName { get; }

        public string PrimaryKeyName { get; }

        public static async Task WithTableAsync(Func<LobQueryStore, Task> test)
        {
            var store = new LobQueryStore();

            try
            {
                await store.CreateTableAsync();
                await test(store);
            }
            finally
            {
                await store.DropTableAsync();
            }
        }

        private async Task CreateTableAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE "{TableName}" (
                    "ID" BIGINT NOT NULL,
                    "LARGE_TEXT" NCLOB NULL,
                    "PAYLOAD" BLOB NULL,
                    CONSTRAINT "{PrimaryKeyName}" PRIMARY KEY ("ID")
                )
                """;
            await command.ExecuteNonQueryAsync();
            _tableCreated = true;
        }

        private async Task DropTableAsync()
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
