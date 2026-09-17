using System.Globalization;
using Dm;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

internal static class DamengTestEnvironment
{
    public const string ConnectionStringVariable = "DAMENG_TEST_CONNECTION_STRING";

    public static string? ConnectionString
        => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static string GetRequiredConnectionString()
        => string.IsNullOrWhiteSpace(ConnectionString)
            ? throw new InvalidOperationException(
                $"Set {ConnectionStringVariable} to run the Dameng functional tests.")
            : ConnectionString;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DamengFactAttribute : FactAttribute
{
    public DamengFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(DamengTestEnvironment.ConnectionString))
        {
            Skip = $"Set {DamengTestEnvironment.ConnectionStringVariable} to run this test.";
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DamengTheoryAttribute : TheoryAttribute
{
    public DamengTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(DamengTestEnvironment.ConnectionString))
        {
            Skip = $"Set {DamengTestEnvironment.ConnectionStringVariable} to run this test.";
        }
    }
}

internal sealed class DamengTestStore
{
    private readonly string _connectionString;
    private bool _tableCreated;

    public DamengTestStore()
    {
        _connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        Suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16].ToUpperInvariant();
        TableName = $"EF10_{Suffix}";
        PrimaryKeyName = $"PK_{Suffix}";
    }

    public string ConnectionString => _connectionString;

    public string Suffix { get; }

    public string TableName { get; }

    public string PrimaryKeyName { get; }

    public async Task CreateEntityTableAsync()
    {
        await using var connection = new DmConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE "{TableName}" (
                "ID" BIGINT IDENTITY(1,1) NOT NULL,
                "NAME" NVARCHAR2(200) NOT NULL,
                "NOTE" NVARCHAR2(200) NULL,
                "VERSION" INT NOT NULL,
                CONSTRAINT "{PrimaryKeyName}" PRIMARY KEY ("ID")
            )
            """;

        await command.ExecuteNonQueryAsync();
        _tableCreated = true;
    }

    public async Task DropObjectsAsync()
    {
        if (!_tableCreated)
        {
            return;
        }

        await using var connection = new DmConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE \"{TableName}\"";
        await command.ExecuteNonQueryAsync();
        _tableCreated = false;
    }

    public static async Task WithEntityTableAsync(Func<DamengTestStore, Task> test)
    {
        var store = new DamengTestStore();

        try
        {
            await store.CreateEntityTableAsync();
            await test(store);
        }
        finally
        {
            await store.DropObjectsAsync();
        }
    }

    public static int AsInt32(object? value)
        => Convert.ToInt32(value, CultureInfo.InvariantCulture);
}
