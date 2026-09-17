using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengSequenceAndTranslationFunctionalTests
{
    [DamengFact]
    public Task SequenceGeneratedKeysAreReadBackWithCurrval()
        => SequenceTranslationStore.WithObjectsAsync(
            async store =>
            {
                var commandLog = new List<string>();

                await using var context = CreateContext(store, commandLog);
                var first = new SequenceTranslationEntity
                {
                    Name = "序列键-第一个",
                    OccurredAt = new DateTime(2026, 7, 23, 10, 11, 12)
                };
                var second = new SequenceTranslationEntity
                {
                    Name = "序列键-第二个",
                    OccurredAt = new DateTime(2026, 7, 24, 10, 11, 12)
                };

                context.Entities.Add(first);
                await context.SaveChangesAsync();
                context.Entities.Add(second);
                await context.SaveChangesAsync();

                Assert.Equal(1_000, first.Id);
                Assert.Equal(1_001, second.Id);
                Assert.Contains(
                    commandLog,
                    message => message.Contains(
                        $"\"{store.SequenceName}\".CURRVAL",
                        StringComparison.Ordinal));

                context.ChangeTracker.Clear();

                Assert.Equal(
                    [first.Id, second.Id],
                    await context.Entities
                        .OrderBy(entity => entity.Id)
                        .Select(entity => entity.Id)
                        .ToListAsync());
            });

    [DamengFact]
    public Task StringDateTimeAndGuidTranslationsExecuteOnServer()
        => SequenceTranslationStore.WithObjectsAsync(
            async store =>
            {
                var commandLog = new List<string>();

                await using var context = CreateContext(store, commandLog);
                var entity = new SequenceTranslationEntity
                {
                    Name = "  达梦Provider后缀  ",
                    OccurredAt = new DateTime(2026, 7, 23, 10, 11, 12)
                };

                context.Entities.Add(entity);
                await context.SaveChangesAsync();
                commandLog.Clear();

                var matchedId = await context.Entities
                    .Where(
                        item => item.Name.Trim().StartsWith("达梦")
                            && item.Name.Contains("Provider")
                            && item.Name.Trim().EndsWith("后缀")
                            && item.Name.Trim().Length == 12
                            && item.OccurredAt.Year == 2026
                            && item.OccurredAt.Month == 7
                            && item.OccurredAt.AddDays(2).Day == 25)
                    .Select(item => item.Id)
                    .SingleAsync();

                Assert.Equal(entity.Id, matchedId);

                var generatedGuid = await context.Entities
                    .Where(item => item.Id == entity.Id)
                    .Select(_ => Guid.NewGuid())
                    .SingleAsync();

                Assert.NotEqual(Guid.Empty, generatedGuid);

                var executedSql = string.Join(Environment.NewLine, commandLog);
                Assert.Contains("TRIM(", executedSql, StringComparison.Ordinal);
                Assert.Contains("INSTR(", executedSql, StringComparison.Ordinal);
                Assert.Contains("RIGHT(", executedSql, StringComparison.Ordinal);
                Assert.Contains("DATEPART(", executedSql, StringComparison.Ordinal);
                Assert.Contains("DATEADD(", executedSql, StringComparison.Ordinal);
                Assert.Contains("NEWID()", executedSql, StringComparison.Ordinal);
            });

    [DamengFact]
    public Task StringCompareAndCompareToExecuteOnServer()
        => SequenceTranslationStore.WithObjectsAsync(
            async store =>
            {
                await using var context = CreateContext(store, []);
                context.Entities.AddRange(
                    new SequenceTranslationEntity
                    {
                        Name = "Alpha",
                        OccurredAt = new DateTime(2026, 7, 23, 10, 11, 12)
                    },
                    new SequenceTranslationEntity
                    {
                        Name = "Omega",
                        OccurredAt = new DateTime(2026, 7, 23, 10, 11, 12)
                    });
                await context.SaveChangesAsync();

#pragma warning disable CA1309 // The default culture Compare overload is the construct under test.
                var compared = await context.Entities
                    .Where(entity => string.Compare(entity.Name, "Mu") > 0)
                    .Select(entity => entity.Name)
                    .ToListAsync();
#pragma warning restore CA1309
                var comparedTo = await context.Entities
                    .Where(entity => entity.Name.CompareTo("Mu") > 0)
                    .Select(entity => entity.Name)
                    .ToListAsync();

                Assert.Equal(["Omega"], compared);
                Assert.Equal(["Omega"], comparedTo);
            });

    private static SequenceTranslationContext CreateContext(
        SequenceTranslationStore store,
        ICollection<string> commandLog)
    {
        var options = new DbContextOptionsBuilder<SequenceTranslationContext>()
            .UseDameng(store.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, SequenceTranslationModelCacheKeyFactory>()
            .LogTo(commandLog.Add)
            .EnableDetailedErrors()
            .Options;

        return new SequenceTranslationContext(
            options,
            store.TableName,
            store.SequenceName);
    }

    private sealed class SequenceTranslationContext(
        DbContextOptions<SequenceTranslationContext> options,
        string tableName,
        string sequenceName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public string SequenceName { get; } = sequenceName;

        public DbSet<SequenceTranslationEntity> Entities
            => Set<SequenceTranslationEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SequenceTranslationEntity>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id);

                    entity.Property(item => item.Id)
                        .HasColumnName("ID")
                        .UseDamengSequence(SequenceName);
                    entity.Property(item => item.Name)
                        .HasColumnName("NAME")
                        .HasMaxLength(200)
                        .IsRequired();
                    entity.Property(item => item.OccurredAt)
                        .HasColumnName("OCCURRED_AT")
                        .HasPrecision(6);
                });
    }

    private sealed class SequenceTranslationModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is SequenceTranslationContext sequenceContext
                ? (
                    context.GetType(),
                    sequenceContext.TableName,
                    sequenceContext.SequenceName,
                    designTime)
                : (context.GetType(), designTime);
    }

    private sealed class SequenceTranslationEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public DateTime OccurredAt { get; set; }
    }

    private sealed class SequenceTranslationStore
    {
        private bool _sequenceCreated;
        private bool _tableCreated;

        private SequenceTranslationStore()
        {
            ConnectionString = DamengTestEnvironment.GetRequiredConnectionString();
            var suffix = Guid.NewGuid()
                .ToString("N", CultureInfo.InvariantCulture)[..12]
                .ToUpperInvariant();
            SequenceName = $"EF10_SQ_{suffix}";
            TableName = $"EF10_ST_{suffix}";
            PrimaryKeyName = $"PK_ST_{suffix}";
        }

        public string ConnectionString { get; }

        public string SequenceName { get; }

        public string TableName { get; }

        public string PrimaryKeyName { get; }

        public static async Task WithObjectsAsync(
            Func<SequenceTranslationStore, Task> test)
        {
            var store = new SequenceTranslationStore();

            try
            {
                await store.CreateObjectsAsync();
                await test(store);
            }
            finally
            {
                await store.DropObjectsAsync();
            }
        }

        private async Task CreateObjectsAsync()
        {
            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE SEQUENCE \"{SequenceName}\" START WITH 1000 INCREMENT BY 1";
            await command.ExecuteNonQueryAsync();
            _sequenceCreated = true;

            command.CommandText =
                $"""
                CREATE TABLE "{TableName}" (
                    "ID" BIGINT DEFAULT ("{SequenceName}".NEXTVAL) NOT NULL,
                    "NAME" NVARCHAR2(200) NOT NULL,
                    "OCCURRED_AT" TIMESTAMP(6) NOT NULL,
                    CONSTRAINT "{PrimaryKeyName}" PRIMARY KEY ("ID")
                )
                """;
            await command.ExecuteNonQueryAsync();
            _tableCreated = true;
        }

        private async Task DropObjectsAsync()
        {
            if (!_tableCreated && !_sequenceCreated)
            {
                return;
            }

            await using var connection = new DmConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();

            if (_tableCreated)
            {
                command.CommandText = $"DROP TABLE \"{TableName}\"";
                await command.ExecuteNonQueryAsync();
                _tableCreated = false;
            }

            if (_sequenceCreated)
            {
                command.CommandText = $"DROP SEQUENCE \"{SequenceName}\"";
                await command.ExecuteNonQueryAsync();
                _sequenceCreated = false;
            }
        }
    }
}
