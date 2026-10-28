using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

namespace W.EntityFrameworkCore.Dameng.Migrations.Internal;

internal sealed class DamengHistoryRepository(HistoryRepositoryDependencies dependencies)
    : HistoryRepository(dependencies), IHistoryRepository
{
    private const int MigrationLockId = 20260723;

    public override LockReleaseBehavior LockReleaseBehavior
        => LockReleaseBehavior.Connection;

    protected override string ExistsSql
    {
        get
        {
            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
            var schema = TableSchema is null
                ? "SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())"
                : stringTypeMapping.GenerateSqlLiteral(TableSchema);

            return new StringBuilder()
                .AppendLine("SELECT COUNT(*)")
                .AppendLine("FROM SYS.SYSOBJECTS O")
                .AppendLine("INNER JOIN SYS.SYSOBJECTS S ON O.SCHID = S.ID")
                .Append("WHERE O.NAME = ")
                .AppendLine(stringTypeMapping.GenerateSqlLiteral(TableName))
                .AppendLine("  AND O.SUBTYPE$ = 'UTAB'")
                .AppendLine("  AND S.TYPE$ = 'SCH'")
                .Append("  AND S.NAME = ")
                .Append(schema)
                .AppendLine(SqlGenerationHelper.StatementTerminator)
                .ToString();
        }
    }

    protected override bool InterpretExistsResult(object? value)
        => value is not null
            && value != DBNull.Value
            && Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0L;

    public override string GetCreateIfNotExistsScript()
        => string.Concat(GetCreateIfNotExistsCommandTexts());

    private List<string> GetCreateIfNotExistsCommandTexts()
    {
        var count = 0;
        var commands = new List<string>();
        foreach (var command in GetCreateCommands())
        {
            var text = command.CommandText;
            var tokens = DamengSqlLexer.Read(text).Take(3).ToArray();
            if (tokens.Length >= 2
                && string.Equals(tokens[0].Text, "CREATE", StringComparison.OrdinalIgnoreCase)
                && string.Equals(tokens[1].Text, "TABLE", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length < 3 || string.Equals(tokens[2].Text, "IF", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The Dameng history CREATE TABLE command has an unexpected guard or shape.");
                count++;
                text = text.Insert(tokens[1].End, " IF NOT EXISTS");
            }
            // Schema guards are complete BEGIN commands; their literals stay opaque.
            commands.Add(text);
        }
        if (count != 1)
            throw new InvalidOperationException("The Dameng history create script must contain exactly one independent CREATE TABLE command.");
        return commands;
    }

    public override string GetBeginIfNotExistsScript(string migrationId)
        => GetBeginIfScript(migrationId, negated: true);

    public override string GetBeginIfExistsScript(string migrationId)
        => GetBeginIfScript(migrationId, negated: false);

    public override string GetEndIfScript()
        => new StringBuilder()
            .AppendLine("    END IF;")
            .AppendLine("END;")
            .Append('/')
            .ToString();

    bool IHistoryRepository.CreateIfNotExists()
    {
        if (Exists())
        {
            return false;
        }

        Dependencies.MigrationCommandExecutor.ExecuteNonQuery(
            GetCreateIfNotExistsCommands(),
            Dependencies.Connection,
            new MigrationExecutionState(),
            commitTransaction: true);

        return true;
    }

    async Task<bool> IHistoryRepository.CreateIfNotExistsAsync(
        CancellationToken cancellationToken)
    {
        if (await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await Dependencies.MigrationCommandExecutor.ExecuteNonQueryAsync(
                GetCreateIfNotExistsCommands(),
                Dependencies.Connection,
                new MigrationExecutionState(),
                commitTransaction: true,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        var databaseLock = CreateMigrationDatabaseLock();
        int result;

        try
        {
            result = Convert.ToInt32(
                CreateAcquireLockCommand().ExecuteScalar(CreateRelationalCommandParameters()),
                CultureInfo.InvariantCulture);
        }
        catch
        {
            TryDispose(databaseLock);
            throw;
        }

        if (result == 0)
        {
            return databaseLock;
        }

        TryDispose(databaseLock);
        throw CreateLockException(result);
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default)
    {
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        var databaseLock = CreateMigrationDatabaseLock();
        int result;

        try
        {
            result = Convert.ToInt32(
                await CreateAcquireLockCommand()
                    .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }
        catch
        {
            await TryDisposeAsync(databaseLock).ConfigureAwait(false);
            throw;
        }

        if (result == 0)
        {
            return databaseLock;
        }

        await TryDisposeAsync(databaseLock).ConfigureAwait(false);
        throw CreateLockException(result);
    }

    private IRelationalCommand CreateAcquireLockCommand()
        => Dependencies.RawSqlCommandBuilder.Build(
            $"SELECT DBMS_LOCK.REQUEST({MigrationLockId}, 6){SqlGenerationHelper.StatementTerminator}");

    private DamengMigrationDatabaseLock CreateMigrationDatabaseLock()
        => new(
            Dependencies.RawSqlCommandBuilder.Build(
                $"SELECT DBMS_LOCK.RELEASE({MigrationLockId}){SqlGenerationHelper.StatementTerminator}"),
            CreateRelationalCommandParameters(),
            this);

    private RelationalCommandParameterObject CreateRelationalCommandParameters()
        => new(
            Dependencies.Connection,
            parameterValues: null,
            readerColumns: null,
            Dependencies.CurrentContext.Context,
            Dependencies.CommandLogger,
            CommandSource.Migrations);

    private IReadOnlyList<MigrationCommand> GetCreateIfNotExistsCommands()
        => Dependencies.MigrationsSqlGenerator.Generate(
            GetCreateIfNotExistsCommandTexts()
                .Select(text => (MigrationOperation)new SqlOperation { Sql = text, SuppressTransaction = true })
                .ToArray());

    private string GetBeginIfScript(string migrationId, bool negated)
    {
        var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

        return new StringBuilder()
            .AppendLine("BEGIN")
            .Append("    IF ")
            .Append(negated ? "NOT " : string.Empty)
            .AppendLine("EXISTS (")
            .AppendLine("        SELECT 1")
            .Append("        FROM ")
            .AppendLine(SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema))
            .Append("        WHERE ")
            .Append(SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName))
            .Append(" = ")
            .AppendLine(stringTypeMapping.GenerateSqlLiteral(migrationId))
            .Append("    ) THEN")
            .ToString();
    }

    private static Exception CreateLockException(int result)
        => result == 1
            ? new TimeoutException("Timed out while acquiring the Dameng migrations lock.")
            : new InvalidOperationException(
                $"Dameng DBMS_LOCK.REQUEST failed with result code {result.ToString(CultureInfo.InvariantCulture)}.");

    private static void TryDispose(DamengMigrationDatabaseLock value)
    {
        try
        {
            value.Dispose();
        }
        catch
        {
        }
    }

    private static async ValueTask TryDisposeAsync(DamengMigrationDatabaseLock value)
    {
        try
        {
            await value.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
