using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using W.EntityFrameworkCore.Dameng.FunctionalTests;

namespace W.EntityFrameworkCore.Dameng.Specification.Tests.TestUtilities;

internal sealed class DamengTestStore : RelationalTestStore
{
    private static readonly ConcurrentDictionary<string, string> SharedSuffixes = new();

    private bool _tableCreated;

    private DamengTestStore(string name, bool shared, string suffix)
        : base(name, shared, new DmConnection(DamengTestEnvironment.GetRequiredConnectionString()))
    {
        TableName = $"EFSP_{suffix}";
        PrimaryKeyName = $"PK_{suffix}";
    }

    public string TableName { get; }

    public string PrimaryKeyName { get; }

    public DbConnection DbConnection
        => Connection;

    public static DamengTestStore Create(string name)
        => new(name, shared: false, CreateSuffix());

    public static DamengTestStore GetOrCreate(string name)
        => new(name, shared: true, SharedSuffixes.GetOrAdd(name, static _ => CreateSuffix()));

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => builder.UseDamengUnderTest(Connection, contextOwnsConnection: false);

    public async Task CreateBasicTypesTableAsync()
    {
        await ExecuteNonQueryAsync(
            $"""
            CREATE TABLE "{TableName}" (
                "ID" BIGINT NOT NULL,
                "BYTE_VALUE" TINYINT NOT NULL,
                "INT_VALUE" INT NOT NULL,
                "LONG_VALUE" BIGINT NOT NULL,
                "DECIMAL_VALUE" DECIMAL(38,20) NOT NULL,
                "TEXT_VALUE" NVARCHAR2(200) NOT NULL,
                "FLAG_VALUE" BIT NOT NULL,
                "GUID_VALUE" CHAR(36) NOT NULL,
                "DATE_VALUE" DATE NOT NULL,
                "TIMESTAMP_VALUE" TIMESTAMP(6) NOT NULL,
                CONSTRAINT "{PrimaryKeyName}" PRIMARY KEY ("ID")
            )
            """);

        _tableCreated = true;
    }

    public async Task DropObjectsAsync()
    {
        if (!_tableCreated)
        {
            return;
        }

        await ExecuteNonQueryAsync($"DROP TABLE \"{TableName}\"");
        _tableCreated = false;
    }

    private async Task ExecuteNonQueryAsync(string commandText)
    {
        var openedHere = Connection.State != ConnectionState.Open;

        try
        {
            if (openedHere)
            {
                await Connection.OpenAsync();
            }

            await using var command = Connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (openedHere)
            {
                await Connection.CloseAsync();
            }
        }
    }

    private static string CreateSuffix()
        => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
