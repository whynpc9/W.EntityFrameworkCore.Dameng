using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengDatabaseCreator : RelationalDatabaseCreator
{
    private const string PhysicalDatabaseMessage
        = "Creating or deleting a Dameng database is a server administration operation. "
        + "Configure an existing database and use migrations or EnsureCreated to manage objects in the current schema.";

    private const string HasTablesSql = "SELECT COUNT(*) FROM USER_TABLES";
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;

    public DamengDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IRawSqlCommandBuilder rawSqlCommandBuilder)
        : base(dependencies)
    {
        _rawSqlCommandBuilder = rawSqlCommandBuilder;
    }

    public override bool Exists()
    {
        var openedHere = Dependencies.Connection.DbConnection.State
            != System.Data.ConnectionState.Open;

        try
        {
            if (openedHere)
            {
                Dependencies.Connection.Open();
            }

            return true;
        }
        finally
        {
            if (openedHere)
            {
                Dependencies.Connection.Close();
            }
        }
    }

    public override async Task<bool> ExistsAsync(
        CancellationToken cancellationToken = default)
    {
        var openedHere = Dependencies.Connection.DbConnection.State
            != System.Data.ConnectionState.Open;

        try
        {
            if (openedHere)
            {
                await Dependencies.Connection
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            if (openedHere)
            {
                await Dependencies.Connection
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
        }
    }

    public override void Create()
        => throw new NotSupportedException(PhysicalDatabaseMessage);

    public override void Delete()
        => throw new NotSupportedException(PhysicalDatabaseMessage);

    public override bool HasTables()
        => Convert.ToInt64(
            _rawSqlCommandBuilder.Build(HasTablesSql).ExecuteScalar(CreateCommandParameters()),
            CultureInfo.InvariantCulture) > 0;

    public override async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = await _rawSqlCommandBuilder.Build(HasTablesSql)
            .ExecuteScalarAsync(CreateCommandParameters(), cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt64(count, CultureInfo.InvariantCulture) > 0;
    }

    private RelationalCommandParameterObject CreateCommandParameters()
        => new(
            Dependencies.Connection,
            parameterValues: null,
            readerColumns: null,
            Dependencies.CurrentContext.Context,
            Dependencies.CommandLogger,
            CommandSource.Migrations);
}
