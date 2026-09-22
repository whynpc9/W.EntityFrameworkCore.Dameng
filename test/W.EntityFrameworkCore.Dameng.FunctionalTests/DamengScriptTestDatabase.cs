using System.Globalization;
using System.Security.Cryptography;
using Dm;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengScriptTestDatabase : IAsyncLifetime
{
    private readonly List<string> _secrets = [];
    private string? _adminConnectionString;
    private string? _password;
    private bool _userCreated;
    private bool _tablespaceCreated;

    public string? ServerBanner { get; private set; }

    public string? InstanceVersion { get; private set; }

    public string? CompatibleMode { get; private set; }

    public long? PageSize { get; private set; }

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var adminConnectionString = DamengTestEnvironment.ConnectionString;
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        _adminConnectionString = adminConnectionString;
        _secrets.Add(adminConnectionString);
        _password = CreatePassword();
        _secrets.Add(_password);

        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tablespace = "EF_TS_" + suffix;
        var user = "EF_U_" + suffix;

        try
        {
            await using var admin = new DmConnection(adminConnectionString);
            await admin.OpenAsync();

            PageSize = await ScalarInt64Async(admin, "SELECT PAGE() FROM DUAL");
            var datafileSizeMb = PageSize switch
            {
                4096 => 16,
                8192 => 32,
                16384 => 64,
                32768 => 128,
                _ => throw new InvalidOperationException(
                    "Unsupported Dameng page size " + PageSize?.ToString(CultureInfo.InvariantCulture) + ".")
            };

            var datafilePath = await ScalarStringAsync(admin, "SELECT PATH FROM V$DATAFILE");
            var directory = Path.GetDirectoryName(datafilePath)
                ?? throw new InvalidOperationException("Dameng did not return a data file directory.");
            var extension = Path.GetExtension(datafilePath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".DBF";
            }

            var datafile = directory.Replace('\\', '/') + "/" + tablespace + extension;
            ServerBanner = await TryScalarStringAsync(admin, "SELECT BANNER FROM V$VERSION");
            InstanceVersion = await TryScalarStringAsync(admin, "SELECT SVR_VERSION FROM V$INSTANCE");
            CompatibleMode = await TryScalarStringAsync(
                admin,
                "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'COMPATIBLE_MODE'");

            await ExecuteAsync(
                admin,
                "CREATE TABLESPACE \"" + tablespace + "\" DATAFILE '"
                + datafile.Replace("'", "''", StringComparison.Ordinal)
                + "' SIZE " + datafileSizeMb.ToString(CultureInfo.InvariantCulture)
                + " AUTOEXTEND OFF");
            _tablespaceCreated = true;
            TablespaceName = tablespace;

            await ExecuteAsync(
                admin,
                "CREATE USER \"" + user + "\" IDENTIFIED BY \"" + _password
                + "\" DEFAULT TABLESPACE \"" + tablespace + "\"");
            _userCreated = true;
            UserName = user;

            await ExecuteAsync(admin, "GRANT RESOURCE TO \"" + user + "\"");
            // History existence checks query SYS.SYSOBJECTS. SOI is the Dameng role that
            // allows a non-DBA account to read those system catalogs.
            await ExecuteAsync(admin, "GRANT SOI TO \"" + user + "\"");

            var builder = new DmConnectionStringBuilder(adminConnectionString)
            {
                ["UID"] = user,
                ["Pwd"] = _password
            };
            ConnectionString = builder.ConnectionString;
            _secrets.Add(ConnectionString);

            await using var userConnection = new DmConnection(ConnectionString);
            await userConnection.OpenAsync();
            var schema = await ScalarStringAsync(
                userConnection,
                "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL");
            if (!string.Equals(schema, user, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Expected the test user schema '" + user + "' but connected to '" + schema + "'.");
            }
        }
        catch (Exception exception)
        {
            var cleanupFailure = await TryDisposeAsync();
            var message = Redact(exception.ToString());
            if (cleanupFailure is not null)
            {
                message += Environment.NewLine + "Cleanup: " + Redact(cleanupFailure.ToString());
            }

            throw new InvalidOperationException(message);
        }
    }

    public async Task DisposeAsync()
    {
        var failure = await TryDisposeAsync();
        if (failure is not null)
        {
            throw new InvalidOperationException(Redact(failure.ToString()));
        }
    }

    public async Task<DmConnection> OpenAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("The Dameng script test database is not initialized.");
        }

        var connection = new DmConnection(ConnectionString);
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public string Redact(string text)
    {
        foreach (var secret in _secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
            }
        }

        return text;
    }

    internal string? TablespaceName { get; private set; }

    internal string? UserName { get; private set; }

    private async Task<Exception?> TryDisposeAsync()
    {
        if (_adminConnectionString is null)
        {
            return null;
        }

        Exception? failure = null;
        try
        {
            await using var admin = new DmConnection(_adminConnectionString);
            await admin.OpenAsync();

            if (_userCreated && UserName is not null)
            {
                try
                {
                    await ExecuteAsync(admin, "DROP USER \"" + UserName + "\" CASCADE");
                    _userCreated = false;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (_tablespaceCreated && TablespaceName is not null)
            {
                try
                {
                    await ExecuteAsync(admin, "DROP TABLESPACE \"" + TablespaceName + "\"");
                    _tablespaceCreated = false;
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        return failure;
    }

    private async Task ExecuteAsync(DmConnection connection, string sql)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(Redact(exception.ToString()));
        }
    }

    private static async Task<long> ScalarInt64Async(DmConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(DmConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("Dameng returned no value.");
    }

    private static async Task<string?> TryScalarStringAsync(DmConnection connection, string sql)
    {
        try
        {
            return await ScalarStringAsync(connection, sql);
        }
        catch (Exception exception) when (exception is DmException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string CreatePassword()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> chars = stackalloc char[24];
        chars[0] = 'A';
        for (var index = 1; index < chars.Length; index++)
        {
            chars[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }
}

[CollectionDefinition(DamengMigrationScriptSet.Name)]
public sealed class DamengMigrationScriptSet : ICollectionFixture<DamengScriptTestDatabase>
{
    public const string Name = "DamengMigrationScripts";
}
