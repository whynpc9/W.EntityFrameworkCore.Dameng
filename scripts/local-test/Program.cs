using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dm;

internal static class Program
{
    private const int AutoextendNextMb = 64;
    private const int MaxSizeMb = 1024;
    private static readonly string Root = FindRoot();
    private static readonly string SecretPath = Path.Combine(Root, ".local-test.secrets.json");

    public static int Main(string[] args)
    {
        try
        {
            return args.Length switch
            {
                1 when args[0] == "provision" => Provision(),
                1 when args[0] == "info" => Info(),
                1 when args[0] == "inspect" => Inspect(),
                1 when args[0] == "grow" => Grow(),
                1 when args[0] == "selftest" => TestLauncher.SelfTest(),
                >= 2 when args[0] == "test" => TestLauncher.Run(args[1..], Root,
                    LoadSecrets(required: false)),
                _ => Usage()
            };
        }
        catch (Exception error)
        {
            // Provider exceptions can include SQL, credentials, and connection metadata.
            Console.Error.WriteLine("Local test setup failed (" + error.GetType().Name
                + "). Inspect only the status and owned names in the local secrets file.");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: scripts/local-test/run.sh provision | info | inspect | grow | selftest | test <unit|functional|specification|admin|probes|all> [--filter <expression>]");
        return 2;
    }

    private static int Provision()
    {
        var document = LoadSecrets(required: true)!;
        var status = ReadString(document, "ProvisioningStatus");
        if (status == "ready")
        {
            ValidateReady(document);
            Console.WriteLine("Persistent Dameng test schema is ready and was validated.");
            return 0;
        }

        if (status != "pending" || !string.IsNullOrEmpty(ReadString(document, "ConnectionString")))
        {
            throw new InvalidOperationException("Provisioning state requires inspection.");
        }

        var adminConnectionString = RequiredString(document, "AdminConnectionString");
        using var admin = new DmConnection(adminConnectionString);
        admin.Open();

        var pageSize = Convert.ToInt64(Scalar(admin, "SELECT PAGE() FROM DUAL"), CultureInfo.InvariantCulture);
        var sizeMb = pageSize switch
        {
            4096 => 16,
            8192 => 32,
            16384 => 64,
            32768 => 128,
            _ => throw new InvalidOperationException("Unsupported page size.")
        };

        var existingDatafile = Convert.ToString(Scalar(admin, "SELECT PATH FROM V$DATAFILE"), CultureInfo.InvariantCulture);
        var directory = Path.GetDirectoryName(existingDatafile);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Datafile directory unavailable.");
        }

        var extension = Path.GetExtension(existingDatafile);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".DBF";
        }

        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        var tablespace = "EF_TEST_TS_" + suffix;
        var user = "EF_TEST_U_" + suffix;
        var datafile = Path.Combine(directory, tablespace + extension).Replace('\\', '/');
        var password = NewPassword();
        var tablespaceCreated = false;
        var userCreated = false;
        var ambiguousCreate = false;

        document["SchemaVersion"] = 1;
        document["ProvisioningStatus"] = "planned";
        document["OwnedTestTablespaceName"] = tablespace;
        document["OwnedTestUserName"] = user;
        document["TestDatafilePath"] = datafile;
        document["AutoextendNextMb"] = AutoextendNextMb;
        document["MaxSizeMb"] = MaxSizeMb;
        SaveSecrets(document);

        try
        {
            ambiguousCreate = true;
            Execute(admin, "CREATE TABLESPACE \"" + tablespace + "\" DATAFILE '"
                + datafile.Replace("'", "''", StringComparison.Ordinal) + "' SIZE "
                + sizeMb.ToString(CultureInfo.InvariantCulture) + " AUTOEXTEND ON NEXT "
                + AutoextendNextMb.ToString(CultureInfo.InvariantCulture) + " MAXSIZE "
                + MaxSizeMb.ToString(CultureInfo.InvariantCulture));
            ambiguousCreate = false;
            tablespaceCreated = true;
            document["ProvisioningStatus"] = "tablespace_created";
            SaveSecrets(document);

            ambiguousCreate = true;
            Execute(admin, "CREATE USER \"" + user + "\" IDENTIFIED BY \""
                + password + "\" DEFAULT TABLESPACE \"" + tablespace + "\"");
            ambiguousCreate = false;
            userCreated = true;
            document["ProvisioningStatus"] = "user_created";
            SaveSecrets(document);

            Execute(admin, "GRANT RESOURCE TO \"" + user + "\"");
            Execute(admin, "GRANT SOI TO \"" + user + "\"");
            document["ProvisioningStatus"] = "grants_applied";
            SaveSecrets(document);

            var builder = new DmConnectionStringBuilder(adminConnectionString)
            {
                ["UID"] = user,
                ["Pwd"] = password
            };
            var testConnectionString = builder.ConnectionString;
            ValidateSchema(testConnectionString, user);
            document["ConnectionString"] = testConnectionString;
            document["ProvisioningStatus"] = "ready";
            SaveSecrets(document);
            Console.WriteLine("Persistent Dameng test schema created and validated.");
            return 0;
        }
        catch
        {
            var cleanupSucceeded = !ambiguousCreate;
            if (userCreated)
            {
                try { Execute(admin, "DROP USER \"" + user + "\" CASCADE"); }
                catch { cleanupSucceeded = false; }
            }
            if (tablespaceCreated)
            {
                try { Execute(admin, "DROP TABLESPACE \"" + tablespace + "\""); }
                catch { cleanupSucceeded = false; }
            }

            if (cleanupSucceeded)
            {
                document["ProvisioningStatus"] = "pending";
                document.Remove("OwnedTestTablespaceName");
                document.Remove("OwnedTestUserName");
                document.Remove("TestDatafilePath");
                document.Remove("AutoextendNextMb");
                document.Remove("MaxSizeMb");
            }
            else
            {
                document["ProvisioningStatus"] = "cleanup_required";
            }

            document["ConnectionString"] = null;
            SaveSecrets(document);
            throw;
        }
    }

    private static int Info()
    {
        var document = LoadSecrets(required: true)!;
        var adminConnectionString = RequiredString(document, "AdminConnectionString");
        var redact = TestLauncher.CreateRedactor(adminConnectionString,
            ReadString(document, "ConnectionString"));
        using var admin = new DmConnection(adminConnectionString);
        admin.Open();

        PrintInfo("BANNER", "SELECT BANNER FROM V$VERSION");
        PrintInfo("SVR_VERSION", "SELECT SVR_VERSION FROM V$INSTANCE");
        PrintInfo("PAGE", "SELECT PAGE() FROM DUAL");
        foreach (var parameter in new[]
        {
            "COMPATIBLE_MODE", "LENGTH_IN_CHAR", "GLOBAL_CHARSET", "CALC_AS_DECIMAL",
            "JSON_MODE", "CLOB_MAX_CALC_LEN"
        })
        {
            PrintInfo(parameter,
                "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = '" + parameter + "'");
        }
        return 0;

        void PrintInfo(string label, string sql)
        {
            try
            {
                var raw = Convert.ToString(Scalar(admin, sql), CultureInfo.InvariantCulture);
                var safe = string.IsNullOrWhiteSpace(raw) ? "未取到"
                    : redact(raw.Replace('\r', ' ').Replace('\n', ' '));
                Console.WriteLine(label + ": " + safe);
            }
            catch
            {
                Console.WriteLine(label + ": 未取到");
            }
        }
    }

    private static int Inspect()
    {
        var document = LoadSecrets(required: true)!;
        var (tablespace, user, datafile, adminConnectionString, testConnectionString) = OwnedSpace(document);
        using var admin = new DmConnection(adminConnectionString);
        admin.Open();
        var space = ReadOwnedSpace(admin, tablespace, datafile);
        using var test = new DmConnection(testConnectionString);
        test.Open();
        VerifyCurrentSchema(test, user);

        var totalMb = PagesToMb(space.TotalPages, space.PageSize);
        var freeMb = PagesToMb(space.FreePages, space.PageSize);
        Console.WriteLine("Owned test space: total=" + FormatMb(totalMb)
            + " MB, used=" + FormatMb(totalMb - freeMb)
            + " MB, free=" + FormatMb(freeMb) + " MB, autoextend="
            + (space.AutoExtend == 1 ? "on" : "off")
            + ", next=" + space.NextMb.ToString(CultureInfo.InvariantCulture)
            + " MB, max=" + space.MaxMb.ToString(CultureInfo.InvariantCulture) + " MB.");
        PrintCount("Tables", test, "SELECT COUNT(*) FROM USER_TABLES");
        PrintCount("Sequences", test, "SELECT COUNT(*) FROM USER_SEQUENCES");
        PrintCount("Recycle-bin objects", admin,
            "SELECT COUNT(*) FROM DBA_RECYCLEBIN WHERE OWNER = '" + user + "'");
        return 0;
    }

    private static int Grow()
    {
        var document = LoadSecrets(required: true)!;
        var (tablespace, user, datafile, adminConnectionString, testConnectionString) = OwnedSpace(document);
        ValidateSchema(testConnectionString, user);
        using var admin = new DmConnection(adminConnectionString);
        admin.Open();
        var before = ReadOwnedSpace(admin, tablespace, datafile);
        if (PagesToMb(before.TotalPages, before.PageSize) > MaxSizeMb)
        {
            throw new InvalidOperationException("Owned datafile already exceeds the bounded maximum.");
        }

        Execute(admin, "ALTER TABLESPACE \"" + tablespace + "\" DATAFILE '"
            + datafile.Replace("'", "''", StringComparison.Ordinal)
            + "' AUTOEXTEND ON NEXT " + AutoextendNextMb.ToString(CultureInfo.InvariantCulture)
            + " MAXSIZE " + MaxSizeMb.ToString(CultureInfo.InvariantCulture));
        var after = ReadOwnedSpace(admin, tablespace, datafile);
        if (after.AutoExtend != 1 || after.NextMb != AutoextendNextMb || after.MaxMb != MaxSizeMb)
        {
            throw new InvalidOperationException("Owned datafile capacity policy did not match the requested bound.");
        }

        document["AutoextendNextMb"] = AutoextendNextMb;
        document["MaxSizeMb"] = MaxSizeMb;
        SaveSecrets(document);
        Console.WriteLine("Owned test datafile now autoextends by 64 MB, capped at 1024 MB.");
        return 0;
    }

    private static (string Tablespace, string User, string Datafile, string Admin, string Test)
        OwnedSpace(JsonObject document)
    {
        if (ReadInt(document, "SchemaVersion") != 1
            || ReadString(document, "ProvisioningStatus") != "ready")
        {
            throw new InvalidOperationException("The persistent test space is not ready.");
        }
        var tablespace = RequiredString(document, "OwnedTestTablespaceName");
        var user = RequiredString(document, "OwnedTestUserName");
        var datafile = RequiredString(document, "TestDatafilePath");
        if (!IsOwnedName(tablespace, "EF_TEST_TS_") || !IsOwnedName(user, "EF_TEST_U_")
            || !string.Equals(Path.GetFileNameWithoutExtension(datafile), tablespace,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(Path.GetDirectoryName(datafile)))
        {
            throw new InvalidOperationException("The owned test object identity is invalid.");
        }
        return (tablespace, user, datafile,
            RequiredString(document, "AdminConnectionString"),
            RequiredString(document, "ConnectionString"));
    }

    private static bool IsOwnedName(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.Ordinal)
            && value.Length == prefix.Length + 20
            && value[prefix.Length..].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private sealed record SpaceSnapshot(long TotalPages, long FreePages, long PageSize,
        long AutoExtend, long NextMb, long MaxMb);

    private static SpaceSnapshot ReadOwnedSpace(DmConnection admin, string tablespace, string datafile)
    {
        using var command = admin.CreateCommand();
        command.CommandText = "SELECT TS.FILE_NUM, DF.PATH, DF.TOTAL_SIZE, DF.FREE_SIZE, "
            + "DF.PAGE_SIZE, DF.AUTO_EXTEND, DF.NEXT_SIZE, DF.MAX_SIZE "
            + "FROM V$TABLESPACE TS JOIN V$DATAFILE DF ON TS.ID = DF.GROUP_ID "
            + "WHERE TS.NAME = '" + tablespace + "'";
        command.CommandTimeout = 30;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Owned tablespace was not found.");
        var fileCount = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
        var actualPath = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture);
        if (fileCount != 1 || !string.Equals(actualPath, datafile, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The recorded datafile does not uniquely belong to the owned tablespace.");
        }
        var snapshot = new SpaceSnapshot(
            Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
            reader.IsDBNull(6) ? 0 : Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture),
            reader.IsDBNull(7) ? 0 : Convert.ToInt64(reader.GetValue(7), CultureInfo.InvariantCulture));
        if (reader.Read()) throw new InvalidOperationException("Owned tablespace has multiple datafile rows.");
        return snapshot;
    }

    private static decimal PagesToMb(long pages, long pageSize)
        => pages * (decimal)pageSize / 1_048_576m;

    private static string FormatMb(decimal mb)
        => mb.ToString("0.##", CultureInfo.InvariantCulture);

    private static void PrintCount(string label, DmConnection connection, string sql)
    {
        try
        {
            var count = Convert.ToInt64(Scalar(connection, sql), CultureInfo.InvariantCulture);
            Console.WriteLine(label + ": " + count.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            Console.WriteLine(label + ": 未取到");
        }
    }

    private static void ValidateReady(JsonObject document)
    {
        if (ReadInt(document, "SchemaVersion") != 1)
        {
            throw new InvalidOperationException("Unsupported local secrets schema.");
        }
        ValidateSchema(RequiredString(document, "ConnectionString"),
            RequiredString(document, "OwnedTestUserName"));
    }

    private static void ValidateSchema(string connectionString, string expectedUser)
    {
        using var test = new DmConnection(connectionString);
        test.Open();
        VerifyCurrentSchema(test, expectedUser);
    }

    private static void VerifyCurrentSchema(DmConnection test, string expectedUser)
    {
        var schema = Convert.ToString(Scalar(test,
            "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"), CultureInfo.InvariantCulture);
        if (!string.Equals(schema, expectedUser, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The connection does not use the owned test schema.");
        }
    }

    private static object? Scalar(DmConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        return command.ExecuteScalar();
    }

    private static void Execute(DmConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        command.ExecuteNonQuery();
    }

    private static string NewPassword()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> result = stackalloc char[32];
        result[0] = 'A';
        for (var index = 1; index < result.Length; index++)
        {
            result[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(result);
    }

    private static JsonObject? LoadSecrets(bool required)
    {
        if (!File.Exists(SecretPath))
        {
            if (required) throw new InvalidOperationException("Local secrets file is missing.");
            return null;
        }
        if (!OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(SecretPath) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite
                | UnixFileMode.OtherExecute)) != 0)
        {
            throw new InvalidOperationException("Local secrets file has broad permissions.");
        }
        return JsonNode.Parse(File.ReadAllText(SecretPath)) as JsonObject
            ?? throw new InvalidOperationException("Invalid local secrets JSON.");
    }

    private static void SaveSecrets(JsonObject document)
    {
        var tempPath = Path.Combine(Root, ".local-test."
            + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".secrets.json");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(tempPath, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                writer.WriteLine();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, SecretPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string RequiredString(JsonObject document, string key)
        => ReadString(document, key) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing local secrets field.");

    private static string? ReadString(JsonObject document, string key)
        => document[key]?.GetValue<string>();

    private static int? ReadInt(JsonObject document, string key)
        => document[key]?.GetValue<int>();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "W.EntityFrameworkCore.Dameng.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Repository root unavailable.");
    }
}
