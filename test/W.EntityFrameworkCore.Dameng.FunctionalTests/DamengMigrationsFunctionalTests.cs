using System.Globalization;
using System.Text;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengMigrationsFunctionalTests
{
    [DamengFact]
    public async Task GeneratedDdlAndHistoryRepositoryExecuteAgainstDameng()
    {
        var suffix = Guid.NewGuid()
            .ToString("N", CultureInfo.InvariantCulture)[..12]
            .ToUpperInvariant();
        var tableName = $"EF10_MIG_{suffix}";
        var sequenceName = $"EF10_SEQ_{suffix}";
        var indexName = $"IX_MIG_{suffix}";
        var primaryKeyName = $"PK_MIG_{suffix}";
        var historyTableName = $"EF10_HIST_{suffix}";
        var migrationId = $"202607230001_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        var options = new DbContextOptionsBuilder<MigrationContext>()
            .UseDameng(
                connectionString,
                damengOptions => damengOptions.MigrationsHistoryTable(historyTableName))
            .ReplaceService<IModelCacheKeyFactory, MigrationModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        await using var context = new MigrationContext(
            options,
            tableName,
            sequenceName,
            indexName,
            primaryKeyName);

        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(source: null, model.GetRelationalModel());
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model);

        Assert.Contains(operations, operation => operation is CreateSequenceOperation);
        Assert.Contains(operations, operation => operation is CreateTableOperation);
        Assert.Contains(operations, operation => operation is CreateIndexOperation);
        Assert.All(
            commands.Where(command => command.CommandText.StartsWith("CREATE", StringComparison.Ordinal)),
            command => Assert.True(command.TransactionSuppressed));
        Assert.All(
            commands.Where(command => !command.CommandText.StartsWith("CREATE", StringComparison.Ordinal)),
            command => Assert.False(command.TransactionSuppressed));

        try
        {
            await context.Database.OpenConnectionAsync();
            foreach (var command in commands)
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            var seeded = await context.Entities
                .AsNoTracking()
                .SingleAsync(item => item.Id == 20);
            Assert.Equal("种子", seeded.Name);
            Assert.Equal("种子", seeded.NormalizedName);
            Assert.Equal(new byte[] { 0, 0xA5, 0xFF }, seeded.Payload);

            var entity = new MigrationEntity { Name = "dameng" };
            context.Entities.Add(entity);
            await context.SaveChangesAsync();
            await context.Entry(entity).ReloadAsync();

            // The explicit seeded identity value advances Dameng's identity
            // counter; with increment 2, the next generated value is 22.
            Assert.Equal(22L, entity.Id);
            Assert.Equal("DAMENG", entity.NormalizedName);
            Assert.Equal(1L, await CountIndexAsync(connectionString, indexName));
            Assert.Equal(41L, await GetNextSequenceValueAsync(connectionString, sequenceName));

            var historyRepository = context.GetService<IHistoryRepository>();
            Assert.False(await historyRepository.ExistsAsync());

            await using (await historyRepository.AcquireDatabaseLockAsync())
            {
            }

            Assert.True(await historyRepository.CreateIfNotExistsAsync());
            Assert.True(await historyRepository.ExistsAsync());
            Assert.False(await historyRepository.CreateIfNotExistsAsync());
            Assert.True(await historyRepository.ExistsAsync());

            var row = new HistoryRow(migrationId, "10.0.12");
            await context.Database.ExecuteSqlRawAsync(historyRepository.GetInsertScript(row));

            var appliedMigrations = await historyRepository.GetAppliedMigrationsAsync();
            var appliedMigration = Assert.Single(appliedMigrations);
            Assert.Equal(migrationId, appliedMigration.MigrationId);
            Assert.Equal("10.0.12", appliedMigration.ProductVersion);

            await context.Database.ExecuteSqlRawAsync(
                historyRepository.GetDeleteScript(migrationId));
            Assert.Empty(await historyRepository.GetAppliedMigrationsAsync());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await DropObjectsAsync(
                connectionString,
                historyTableName,
                tableName,
                sequenceName);
        }
    }

    [DamengFact]
    public async Task IdempotentCommandsApplyIdentitySeedAndHistoryOnlyOnce()
    {
        var suffix = Guid.NewGuid()
            .ToString("N", CultureInfo.InvariantCulture)[..12]
            .ToUpperInvariant();
        var tableName = $"EF10_IDEM_{suffix}";
        var sequenceName = $"EF10_IDSQ_{suffix}";
        var indexName = $"IX_IDEM_{suffix}";
        var primaryKeyName = $"PK_IDEM_{suffix}";
        var historyTableName = $"EF10_IDH_{suffix}";
        var migrationId = $"202607230002_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        var options = new DbContextOptionsBuilder<MigrationContext>()
            .UseDameng(
                connectionString,
                damengOptions => damengOptions.MigrationsHistoryTable(historyTableName))
            .ReplaceService<IModelCacheKeyFactory, MigrationModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        await using var context = new MigrationContext(
            options,
            tableName,
            sequenceName,
            indexName,
            primaryKeyName);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(source: null, model.GetRelationalModel());
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(
                operations,
                model,
                MigrationsSqlGenerationOptions.Idempotent);
        var historyRepository = context.GetService<IHistoryRepository>();

        try
        {
            Assert.True(await historyRepository.CreateIfNotExistsAsync());

            var endIfScript = historyRepository.GetEndIfScript();
            var disqlTerminatorIndex = endIfScript.LastIndexOf('/');
            Assert.True(disqlTerminatorIndex >= 0);

            var block = new StringBuilder()
                .AppendLine(historyRepository.GetBeginIfNotExistsScript(migrationId));
            foreach (var command in commands)
            {
                block.AppendLine(command.CommandText);
            }

            block
                .AppendLine(
                    historyRepository.GetInsertScript(
                        new HistoryRow(migrationId, "10.0.12")))
                .Append(endIfScript.AsSpan(0, disqlTerminatorIndex));

            var commandText = block.ToString();
            await context.Database.ExecuteSqlRawAsync(commandText);
            await context.Database.ExecuteSqlRawAsync(commandText);

            var seeded = await context.Entities
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == 20);
            Assert.Equal("种子", seeded.Name);
            Assert.Equal(new byte[] { 0, 0xA5, 0xFF }, seeded.Payload);
            var appliedMigration = Assert.Single(
                await historyRepository.GetAppliedMigrationsAsync());
            Assert.Equal(migrationId, appliedMigration.MigrationId);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await DropObjectsAsync(
                connectionString,
                historyTableName,
                tableName,
                sequenceName);
        }
    }

    private static async Task<long> CountIndexAsync(
        string connectionString,
        string indexName)
    {
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM USER_INDEXES WHERE INDEX_NAME = :index_name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "index_name";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture);
    }

    private static async Task<long> GetNextSequenceValueAsync(
        string connectionString,
        string sequenceName)
    {
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{sequenceName}\".NEXTVAL";

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture);
    }

    private static async Task DropObjectsAsync(
        string connectionString,
        string historyTableName,
        string tableName,
        string sequenceName)
    {
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();

        await DropIfExistsAsync(
            connection,
            "USER_TABLES",
            "TABLE_NAME",
            historyTableName,
            $"DROP TABLE \"{historyTableName}\"");
        await DropIfExistsAsync(
            connection,
            "USER_TABLES",
            "TABLE_NAME",
            tableName,
            $"DROP TABLE \"{tableName}\"");
        await DropIfExistsAsync(
            connection,
            "USER_SEQUENCES",
            "SEQUENCE_NAME",
            sequenceName,
            $"DROP SEQUENCE \"{sequenceName}\"");
    }

    private static async Task DropIfExistsAsync(
        DmConnection connection,
        string catalogView,
        string nameColumn,
        string objectName,
        string dropSql)
    {
        if (await CountCatalogObjectAsync(connection, catalogView, nameColumn, objectName) == 0)
        {
            return;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = dropSql;
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(
            0L,
            await CountCatalogObjectAsync(connection, catalogView, nameColumn, objectName));
    }

    private static async Task<long> CountCatalogObjectAsync(
        DmConnection connection,
        string catalogView,
        string nameColumn,
        string objectName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {catalogView} WHERE {nameColumn} = :object_name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "object_name";
        parameter.Value = objectName;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture);
    }

    private sealed class MigrationContext(
        DbContextOptions<MigrationContext> options,
        string tableName,
        string sequenceName,
        string indexName,
        string primaryKeyName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public string SequenceName { get; } = sequenceName;

        public string IndexName { get; } = indexName;

        public string PrimaryKeyName { get; } = primaryKeyName;

        public DbSet<MigrationEntity> Entities => Set<MigrationEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>(SequenceName)
                .StartsAt(41)
                .IncrementsBy(3);

            modelBuilder.Entity<MigrationEntity>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id)
                        .HasName(PrimaryKeyName);
                    entity.HasIndex(item => item.Name)
                        .HasDatabaseName(IndexName);

                    entity.Property(item => item.Id)
                        .HasColumnName("ID")
                        .UseDamengIdentityColumn(seed: 10, increment: 2);
                    entity.Property(item => item.Name)
                        .HasColumnName("NAME")
                        .HasMaxLength(64)
                        .IsRequired();
                    entity.Property(item => item.NormalizedName)
                        .HasColumnName("NORMALIZED_NAME")
                        .HasComputedColumnSql("UPPER(\"NAME\")", stored: false);
                    entity.Property(item => item.Payload)
                        .HasColumnName("PAYLOAD")
                        .HasMaxLength(8)
                        .IsRequired();

                    entity.HasData(
                        new MigrationEntity
                        {
                            Id = 20,
                            Name = "种子",
                            Payload = [0, 0xA5, 0xFF]
                        });
                });
        }
    }

    private sealed class MigrationModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is MigrationContext migrationContext
                ? (
                    context.GetType(),
                    migrationContext.TableName,
                    migrationContext.SequenceName,
                    migrationContext.IndexName,
                    migrationContext.PrimaryKeyName,
                    designTime)
                : (context.GetType(), designTime);
    }

    private sealed class MigrationEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public string? NormalizedName { get; set; }

        public byte[] Payload { get; set; } = [];
    }
}
