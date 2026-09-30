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

    [DamengFact]
    public async Task CommentsExecuteAndRoundTripAgainstDameng()
    {
        var suffix = Guid.NewGuid()
            .ToString("N", CultureInfo.InvariantCulture)[..12]
            .ToUpperInvariant();
        var tableName = $"EF10_CMT_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseDameng(connectionString)
            .ReplaceService<IModelCacheKeyFactory, CommentModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        try
        {
            await using var contextV1 = new CommentContext(options, tableName, "首版注释", "备注 '引号'");
            var modelV1 = contextV1.GetService<IDesignTimeModel>().Model;
            var createCommands = contextV1.GetService<IMigrationsSqlGenerator>()
                .Generate(
                    contextV1.GetService<IMigrationsModelDiffer>()
                        .GetDifferences(source: null, modelV1.GetRelationalModel()),
                    modelV1);

            Assert.Contains(
                createCommands,
                command => command.CommandText.StartsWith("COMMENT ON TABLE", StringComparison.Ordinal)
                    && command.TransactionSuppressed);
            Assert.Contains(
                createCommands,
                command => command.CommandText.StartsWith("COMMENT ON COLUMN", StringComparison.Ordinal)
                    && command.TransactionSuppressed);

            await contextV1.Database.OpenConnectionAsync();
            foreach (var command in createCommands)
            {
                await contextV1.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            Assert.Equal("首版注释", await ReadTableCommentAsync(connectionString, tableName));
            Assert.Equal("备注 '引号'", await ReadNoteCommentAsync(connectionString, tableName));

            await using var contextV2 = new CommentContext(options, tableName, "覆盖注释", null);
            var modelV2 = contextV2.GetService<IDesignTimeModel>().Model;
            var alterCommands = contextV2.GetService<IMigrationsSqlGenerator>()
                .Generate(
                    contextV2.GetService<IMigrationsModelDiffer>()
                        .GetDifferences(modelV1.GetRelationalModel(), modelV2.GetRelationalModel()),
                    modelV2);

            foreach (var command in alterCommands)
            {
                await contextV2.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            Assert.Equal("覆盖注释", await ReadTableCommentAsync(connectionString, tableName));
            Assert.Equal(string.Empty, await ReadNoteCommentAsync(connectionString, tableName));
        }
        finally
        {
            await using var connection = new DmConnection(connectionString);
            await connection.OpenAsync();
            await DropIfExistsAsync(
                connection,
                "USER_TABLES",
                "TABLE_NAME",
                tableName,
                $"DROP TABLE \"{tableName}\"");
        }
    }

    [DamengFact]
    public async Task IdempotentScriptCarriesMultilineCommentsAsSingleBatches()
    {
        var suffix = Guid.NewGuid()
            .ToString("N", CultureInfo.InvariantCulture)[..12]
            .ToUpperInvariant();
        var tableName = $"EF10_CML_{suffix}";
        var historyTableName = $"EF10_CMH_{suffix}";
        var migrationId = $"202609300001_{suffix}";
        var tableComment = "首行注释\nEND;\n仍属同一条注释";
        var columnComment = "列注释\nBEGIN\n尾行";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        var options = new DbContextOptionsBuilder<CommentContext>()
            .UseDameng(
                connectionString,
                damengOptions => damengOptions.MigrationsHistoryTable(historyTableName))
            .ReplaceService<IModelCacheKeyFactory, CommentModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        try
        {
            await using var context = new CommentContext(options, tableName, tableComment, columnComment);
            var model = context.GetService<IDesignTimeModel>().Model;
            var commands = context.GetService<IMigrationsSqlGenerator>()
                .Generate(
                    context.GetService<IMigrationsModelDiffer>()
                        .GetDifferences(source: null, model.GetRelationalModel()),
                    model,
                    MigrationsSqlGenerationOptions.Script | MigrationsSqlGenerationOptions.Idempotent);

            var historyRepository = context.GetService<IHistoryRepository>();
            var endIfScript = historyRepository.GetEndIfScript();
            var script = new StringBuilder()
                .AppendLine(historyRepository.GetCreateIfNotExistsScript())
                .AppendLine("/")
                .AppendLine(historyRepository.GetBeginIfNotExistsScript(migrationId).TrimEnd());
            foreach (var command in commands)
            {
                script.AppendLine(command.CommandText);
            }

            script
                .AppendLine(historyRepository.GetInsertScript(new HistoryRow(migrationId, "10.0.12")))
                .Append(endIfScript);

            await using (var connection = new DmConnection(connectionString))
            {
                await connection.OpenAsync();
                await DamengScriptExecutor.ExecuteAsync(
                    connection,
                    script.ToString(),
                    idempotent: true,
                    redact: text => text);
            }

            Assert.Equal(tableComment, await ReadTableCommentAsync(connectionString, tableName));

            await using var columnConnection = new DmConnection(connectionString);
            await columnConnection.OpenAsync();
            await using var readback = columnConnection.CreateCommand();
            readback.CommandText =
                "SELECT COMMENTS FROM USER_COL_COMMENTS WHERE TABLE_NAME = :table_name AND COLUMN_NAME = 'NOTE'";
            var parameter = readback.CreateParameter();
            parameter.ParameterName = "table_name";
            parameter.Value = tableName;
            readback.Parameters.Add(parameter);
            Assert.Equal(
                columnComment,
                Convert.ToString(await readback.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
        finally
        {
            await using var connection = new DmConnection(connectionString);
            await connection.OpenAsync();
            await DropIfExistsAsync(
                connection,
                "USER_TABLES",
                "TABLE_NAME",
                tableName,
                $"DROP TABLE \"{tableName}\"");
            await DropIfExistsAsync(
                connection,
                "USER_TABLES",
                "TABLE_NAME",
                historyTableName,
                $"DROP TABLE \"{historyTableName}\"");
        }
    }

    [DamengFact]
    public async Task EnsureSchemaGuardSkipsExistingCurrentSchema()
    {
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        string currentSchema;
        await using (var connection = new DmConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual";
            currentSchema = Convert.ToString(
                await schemaCommand.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture)!;
        }

        var options = new DbContextOptionsBuilder<EmptyContext>()
            .UseDameng(connectionString)
            .Options;
        await using var context = new EmptyContext(options);
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var command = Assert.Single(
            generator.Generate([new EnsureSchemaOperation { Name = currentSchema }]));
        Assert.StartsWith("BEGIN", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("TYPE$ = 'SCH'", command.CommandText, StringComparison.Ordinal);

        var idempotentCommand = Assert.Single(
            generator.Generate(
                [new EnsureSchemaOperation { Name = currentSchema }],
                options: MigrationsSqlGenerationOptions.Idempotent));
        Assert.StartsWith("BEGIN", idempotentCommand.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EXECUTE IMMEDIATE 'BEGIN",
            idempotentCommand.CommandText,
            StringComparison.Ordinal);

        // The guarded block must succeed twice without CREATE SCHEMA privileges:
        // the catalog check short-circuits before EXECUTE IMMEDIATE runs.
        await using var execution = new DmConnection(connectionString);
        await execution.OpenAsync();
        var step = 0;
        foreach (var text in new[]
        {
            command.CommandText,
            command.CommandText,
            idempotentCommand.CommandText,
            idempotentCommand.CommandText
        })
        {
            step++;
            try
            {
                await using var batch = execution.CreateCommand();
                batch.CommandText = text;
                await batch.ExecuteNonQueryAsync();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"EnsureSchema execution step {step} failed. Text: "
                    + text.Replace("\r", "\\r", StringComparison.Ordinal)
                        .Replace("\n", "\\n", StringComparison.Ordinal),
                    exception);
            }
        }
    }

    private static async Task<string?> ReadTableCommentAsync(string connectionString, string tableName)
    {
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COMMENTS FROM USER_TAB_COMMENTS WHERE TABLE_NAME = :table_name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table_name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadNoteCommentAsync(string connectionString, string tableName)
    {
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COMMENTS FROM USER_COL_COMMENTS WHERE TABLE_NAME = :table_name AND COLUMN_NAME = 'NOTE'";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table_name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private sealed class EmptyContext(DbContextOptions<EmptyContext> options) : DbContext(options);

    [DamengFact]
    public async Task AnsiSizedStringColumnsUseCharSemanticsAndHoldMultibyteText()
    {
        var suffix = Guid.NewGuid()
            .ToString("N", CultureInfo.InvariantCulture)[..12]
            .ToUpperInvariant();
        var tableName = $"EF10_CHR_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        var options = new DbContextOptionsBuilder<AnsiStringContext>()
            .UseDameng(connectionString)
            .ReplaceService<IModelCacheKeyFactory, AnsiStringModelCacheKeyFactory>()
            .EnableDetailedErrors()
            .Options;

        await using var context = new AnsiStringContext(options, tableName);
        var model = context.GetService<IDesignTimeModel>().Model;
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(
                context.GetService<IMigrationsModelDiffer>()
                    .GetDifferences(source: null, model.GetRelationalModel()),
                model);

        var createTable = Assert.Single(
            commands,
            command => command.CommandText.StartsWith("CREATE TABLE", StringComparison.Ordinal));
        Assert.Contains("\"CODE\" VARCHAR2(3 CHAR)", createTable.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"INITIALS\" CHAR(2 CHAR)", createTable.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"LONG_CODE\" VARCHAR2(1000 CHAR)", createTable.CommandText, StringComparison.Ordinal);

        try
        {
            await context.Database.OpenConnectionAsync();
            foreach (var command in commands)
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            var longCode = new string('中', 1000);
            context.Entities.Add(
                new AnsiStringEntity { Code = "中文字", Initials = "中文", LongCode = longCode });
            await context.SaveChangesAsync();

            var readback = await context.Entities
                .AsNoTracking()
                .SingleAsync(entity => entity.Code == "中文字");
            Assert.Equal("中文", readback.Initials);
            Assert.Equal(longCode, readback.LongCode);

            await using var connection = new DmConnection(connectionString);
            await connection.OpenAsync();
            await using var catalog = connection.CreateCommand();
            catalog.CommandText =
                "SELECT CHAR_USED FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :table_name AND COLUMN_NAME = 'CODE'";
            var parameter = catalog.CreateParameter();
            parameter.ParameterName = "table_name";
            parameter.Value = tableName;
            catalog.Parameters.Add(parameter);
            Assert.Equal(
                "C",
                Convert.ToString(await catalog.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await DropObjectsAsync(connectionString, tableName, tableName, tableName);
        }
    }

    private sealed class AnsiStringContext(
        DbContextOptions<AnsiStringContext> options,
        string tableName)
        : DbContext(options)
    {
        public string TableName { get; } = tableName;

        public DbSet<AnsiStringEntity> Entities => Set<AnsiStringEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AnsiStringEntity>(
                entity =>
                {
                    entity.ToTable(TableName);
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Id).HasColumnName("ID");
                    entity.Property(item => item.Code)
                        .HasColumnName("CODE")
                        .HasMaxLength(3)
                        .IsUnicode(false);
                    entity.Property(item => item.Initials)
                        .HasColumnName("INITIALS")
                        .HasMaxLength(2)
                        .IsUnicode(false)
                        .IsFixedLength();
                    entity.Property(item => item.LongCode)
                        .HasColumnName("LONG_CODE")
                        .HasMaxLength(1000)
                        .IsUnicode(false);
                });
    }

    private sealed class AnsiStringModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is AnsiStringContext ansiContext
                ? (context.GetType(), ansiContext.TableName, designTime)
                : (context.GetType(), designTime);
    }

    private sealed class AnsiStringEntity
    {
        public int Id { get; set; }

        public string? Code { get; set; }

        public string? Initials { get; set; }

        public string? LongCode { get; set; }
    }

    private sealed class CommentContext(
        DbContextOptions<CommentContext> options,
        string tableName,
        string? tableComment,
        string? noteComment)
        : DbContext(options)
    {
        public DbSet<CommentEntity> Entities => Set<CommentEntity>();

        public string TableName { get; } = tableName;

        public string? TableComment { get; } = tableComment;

        public string? NoteComment { get; } = noteComment;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CommentEntity>(
                entity =>
                {
                    entity.ToTable(
                        TableName,
                        table =>
                        {
                            if (TableComment is not null)
                            {
                                table.HasComment(TableComment);
                            }
                        });
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Id).HasColumnName("ID");
                    entity.Property(item => item.Note)
                        .HasColumnName("NOTE")
                        .HasMaxLength(50);

                    if (NoteComment is not null)
                    {
                        entity.Property(item => item.Note).HasComment(NoteComment);
                    }
                });
    }

    private sealed class CommentModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is CommentContext commentContext
                ? (
                    context.GetType(),
                    commentContext.TableName,
                    commentContext.TableComment,
                    commentContext.NoteComment,
                    designTime)
                : (context.GetType(), designTime);
    }

    private sealed class CommentEntity
    {
        public int Id { get; set; }

        public string? Note { get; set; }
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
