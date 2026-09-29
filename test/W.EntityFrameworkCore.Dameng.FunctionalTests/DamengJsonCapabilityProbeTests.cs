using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dm;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// Q4 候选 SQL 的黑盒测量。case 成功运行不代表相应公共 API 已获得支持。
/// </summary>
public sealed class DamengJsonCapabilityProbeTests(ITestOutputHelper output)
{
    private const int CommandTimeoutSeconds = 15;
    private const string Policy = "NULL ON EMPTY ERROR ON ERROR";
    private static readonly JsonSerializerOptions RawJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task ReturningTypesScalarTextAndQuotedPaths()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var cases = new List<ProbeCase>
        {
            Value("number.integer", """{"v":42}""", "v", "NUMBER"),
            Value("number.fractional", """{"v":1.5}""", "v", "NUMBER"),
            Value("number.numeric_string", """{"v":"42"}""", "v", "NUMBER"),
            Value("number.text_type_error", """{"v":"abc"}""", "v", "NUMBER"),
            Value("number.boolean_type_error", """{"v":true}""", "v", "NUMBER"),
            Value("number.int32_overflow", """{"v":2147483648}""", "v", "NUMBER(10,0)"),
            Value("number.scale_zero_fractional", """{"v":1.5}""", "v", "NUMBER(10,0)"),
            Value("number.scale_zero_numeric_string", """{"v":"42"}""", "v", "NUMBER(10,0)"),
            Value("decimal.integer", """{"v":42}""", "v", "DECIMAL(10,0)"),
            Value("decimal.fractional_to_integer", """{"v":1.5}""", "v", "DECIMAL(10,0)"),
            Value("decimal.numeric_string", """{"v":"42"}""", "v", "DECIMAL(10,0)"),
            Value("decimal.text_type_error", """{"v":"abc"}""", "v", "DECIMAL(10,0)"),
            Value("decimal.boolean_type_error", """{"v":true}""", "v", "DECIMAL(10,0)"),
            Value("decimal.int32_overflow", """{"v":2147483648}""", "v", "DECIMAL(10,0)"),
            Value("decimal.scale_two", """{"v":1.5}""", "v", "DECIMAL(10,2)"),
            Value("text.string", """{"v":"文字"}""", "v", "VARCHAR2(100)"),
            Value("text.number", """{"v":12.50}""", "v", "VARCHAR2(100)"),
            Value("text.numeric_string", """{"v":"12.50"}""", "v", "VARCHAR2(100)"),
            Value("text.boolean_true", """{"v":true}""", "v", "VARCHAR2(100)"),
            Value("text.boolean_false", """{"v":false}""", "v", "VARCHAR2(100)"),
            Value("text.empty_string", """{"v":""}""", "v", "VARCHAR2(100)"),
            Value("text.unicode", """{"v":"汉字😀"}""", "v", "VARCHAR2(100)"),
            Value("text.unicode_length_one", """{"v":"汉"}""", "v", "VARCHAR2(1)"),
            Value("text.unicode_length_three", """{"v":"汉"}""", "v", "VARCHAR2(3)"),
            Value("text.json_null", """{"v":null}""", "v", "VARCHAR2(100)"),
            Value("text.missing", """{"v":1}""", "missing", "VARCHAR2(100)"),
            Value("text.object_error", """{"v":{"x":1}}""", "v", "VARCHAR2(100)"),
            Value("text.array_error", """{"v":[1]}""", "v", "VARCHAR2(100)"),
            Value("text.explicit_length_overflow", """{"v":"abcdef"}""", "v", "VARCHAR2(4)"),
            Value("path.dotted", """{"a.b":"dot"}""", "a.b", "VARCHAR2(100)"),
            Value("path.quote", """{"a\"b":"quote"}""", "a\"b", "VARCHAR2(100)"),
            Value("path.backslash", """{"a\\b":"slash"}""", "a\\b", "VARCHAR2(100)"),
            Value("path.apostrophe", """{"a'b":"apostrophe"}""", "a'b", "VARCHAR2(100)"),
            Value("path.slash", """{"a/b":"slash"}""", "a/b", "VARCHAR2(100)"),
            new("path.slash_escaped", "SELECT JSON_VALUE(" + SqlLiteral("""{"a/b":"slash"}""") + ", "
                + SqlLiteral("$.\"a\\/b\"") + " RETURNING VARCHAR2(100) " + Policy + ") FROM dual"),
            Value("path.tilde", """{"a~b":"tilde"}""", "a~b", "VARCHAR2(100)"),
            Value("path.tab", """{"a\tb":"tab"}""", "a\tb", "VARCHAR2(100)"),
            Value("path.newline", """{"a\nb":"newline"}""", "a\nb", "VARCHAR2(100)"),
            Value("path.chinese", """{"中文":"chinese"}""", "中文", "VARCHAR2(100)"),
            new("path.nested", "SELECT " + JsonValue(SqlLiteral("""{"a":{"b":"nested"}}"""), ["a", "b"], "VARCHAR2(100)") + " FROM dual")
        };

        await RunAsync(connection, cases);
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task EmptyStringDistinctionAndNativeUnicode()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var tableName = "EF10_JE_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var created = false;
        try
        {
            await ExecuteNonQueryAsync(connection, $"CREATE TABLE \"{tableName}\" (\"ID\" INT, \"DOC\" JSON)");
            created = true;

            var escapedDocument = JsonSerializer.Serialize(new { v = "汉字😀" });
            var rawDocument = JsonSerializer.Serialize(new { v = "汉字😀" }, RawJsonOptions);
            await InsertDocumentAsync(connection, tableName, 1, escapedDocument);
            await InsertDocumentAsync(connection, tableName, 2, rawDocument);
            await ExecuteNonQueryAsync(connection,
                $"INSERT INTO \"{tableName}\" (\"ID\", \"DOC\") VALUES (3, {SqlLiteral(rawDocument)})");

            var cases = new List<ProbeCase>
            {
                new("empty.json_extract", """SELECT JSON_EXTRACT('{"v":""}', '$."v"') FROM dual"""),
                new("empty.json_query", """SELECT JSON_QUERY('{"v":""}', '$."v"') FROM dual"""),
                new("empty.json_exists", """SELECT JSON_EXISTS('{"v":""}', '$."v"') FROM dual"""),
                new("empty.json_type", """SELECT JSON_TYPE(JSON_EXTRACT('{"v":""}', '$."v"')) FROM dual"""),
                new("null.json_extract", """SELECT JSON_EXTRACT('{"v":null}', '$."v"') FROM dual"""),
                new("null.json_exists", """SELECT JSON_EXISTS('{"v":null}', '$."v"') FROM dual"""),
                new("missing.json_extract", """SELECT JSON_EXTRACT('{"v":1}', '$."missing"') FROM dual"""),
                new("missing.json_exists", """SELECT JSON_EXISTS('{"v":1}', '$."missing"') FROM dual"""),
                new("unicode.escaped_parameter", "SELECT " + JsonValue("\"DOC\"", ["v"], "VARCHAR2(100)")
                    + $" FROM \"{tableName}\" WHERE \"ID\" = 1"),
                new("unicode.raw_parameter", "SELECT " + JsonValue("\"DOC\"", ["v"], "VARCHAR2(100)")
                    + $" FROM \"{tableName}\" WHERE \"ID\" = 2"),
                new("unicode.raw_literal", "SELECT " + JsonValue("\"DOC\"", ["v"], "VARCHAR2(100)")
                    + $" FROM \"{tableName}\" WHERE \"ID\" = 3"),
                new("unicode.escaped_parameter_raw_column", $"SELECT \"DOC\" FROM \"{tableName}\" WHERE \"ID\" = 1"),
                new("unicode.raw_parameter_raw_column", $"SELECT \"DOC\" FROM \"{tableName}\" WHERE \"ID\" = 2"),
                new("unicode.raw_literal_raw_column", $"SELECT \"DOC\" FROM \"{tableName}\" WHERE \"ID\" = 3"),
                new("unicode.escaped_parameter_cast_column", $"SELECT CAST(\"DOC\" AS VARCHAR2(100)) FROM \"{tableName}\" WHERE \"ID\" = 1"),
                new("unicode.raw_parameter_cast_column", $"SELECT CAST(\"DOC\" AS VARCHAR2(100)) FROM \"{tableName}\" WHERE \"ID\" = 2"),
                new("unicode.raw_literal_cast_column", $"SELECT CAST(\"DOC\" AS VARCHAR2(100)) FROM \"{tableName}\" WHERE \"ID\" = 3")
            };
            await RunAsync(connection, cases);
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task NativeJsonColumnAndLengthBoundary()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var tableName = "EF10_JP_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var created = false;
        try
        {
            await ExecuteNonQueryAsync(connection, $"CREATE TABLE \"{tableName}\" (\"ID\" INT, \"DOC\" JSON)");
            created = true;

            var document = JsonSerializer.Serialize(new
            {
                text = "汉字😀",
                number = 42,
                fraction = 1.5,
                numericString = "42",
                boolean = true,
                empty = "",
                nested = new { value = "nested" },
                longValue = new string('x', 4001)
            });
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO \"{tableName}\" (\"ID\", \"DOC\") VALUES (1, :document)";
                insert.CommandTimeout = CommandTimeoutSeconds;
                var parameter = insert.CreateParameter();
                parameter.ParameterName = "document";
                parameter.DbType = DbType.String;
                parameter.Value = document;
                insert.Parameters.Add(parameter);
                await insert.ExecuteNonQueryAsync();
            }

            var source = $"\"{tableName}\" WHERE \"ID\" = 1";
            await RunAsync(connection,
            [
                Column("column.text", "text", "VARCHAR2(100)", source),
                Column("column.nested", "nested.value", "VARCHAR2(100)", source),
                Column("column.number_text", "number", "VARCHAR2(100)", source),
                Column("column.boolean_text", "boolean", "VARCHAR2(100)", source),
                Column("column.empty_text", "empty", "VARCHAR2(100)", source),
                Column("column.missing", "missing", "VARCHAR2(100)", source),
                Column("column.fraction_to_decimal_zero", "fraction", "DECIMAL(10,0)", source),
                Column("column.numeric_string_to_decimal", "numericString", "DECIMAL(10,0)", source),
                Column("column.long_exact", "longValue", "VARCHAR2(4001)", source),
                Column("column.long_at_8000", "longValue", "VARCHAR2(8000)", source),
                Column("column.long_overflow", "longValue", "VARCHAR2(4000)", source)
            ]);
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    private static ProbeCase Value(string id, string json, string property, string returning)
        => new(id, "SELECT " + JsonValue(SqlLiteral(json), [property], returning) + " FROM dual");

    private static ProbeCase Column(string id, string propertyPath, string returning, string source)
        => new(id, "SELECT " + JsonValue("\"DOC\"", propertyPath.Split('.'), returning) + " FROM " + source);

    private static string JsonValue(string document, IReadOnlyList<string> propertyPath, string returning)
    {
        var path = "$" + string.Concat(propertyPath.Select(segment => "." + JsonSerializer.Serialize(segment)));
        return $"JSON_VALUE({document}, {SqlLiteral(path)} RETURNING {returning} {Policy})";
    }

    private static string SqlLiteral(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertDocumentAsync(DbConnection connection, string tableName, int id, string document)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO \"{tableName}\" (\"ID\", \"DOC\") VALUES ({id}, :document)";
        command.CommandTimeout = CommandTimeoutSeconds;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "document";
        parameter.DbType = DbType.String;
        parameter.Value = document;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RunAsync(DbConnection connection, IReadOnlyList<ProbeCase> cases)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var probe in cases)
        {
            var status = await ExecuteCaseAsync(connection, probe);
            counts[status] = counts.GetValueOrDefault(status) + 1;
        }

        output.WriteLine(JsonSerializer.Serialize(new { Status = "summary", Cases = cases.Count, Counts = counts }));
        Assert.Equal(cases.Count, counts.Values.Sum());
    }

    private async Task<string> ExecuteCaseAsync(DbConnection connection, ProbeCase probe)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = probe.Sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        var watch = Stopwatch.StartNew();
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                output.WriteLine(JsonSerializer.Serialize(new { probe.Id, Status = "no_row", ElapsedMs = watch.ElapsedMilliseconds }));
                return "no_row";
            }

            var value = reader.GetValue(0);
            var display = value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                probe.Id,
                Status = "value",
                Value = display is { Length: > 120 } ? display[..120] : display,
                ValueLength = display?.Length,
                ClrType = value is DBNull ? null : value.GetType().FullName,
                DataType = reader.GetDataTypeName(0),
                ElapsedMs = watch.ElapsedMilliseconds
            }));
            return "value";
        }
        catch (Exception error) when (error is DbException or InvalidCastException or FormatException or OverflowException)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                probe.Id,
                Status = error is DbException ? "sql_error" : "read_error",
                ErrorType = error.GetType().Name,
                ErrorCode = (error as DbException)?.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            }));
            return error is DbException ? "sql_error" : "read_error";
        }
    }

    private static string SafeSummary(string message)
    {
        var firstLine = message.Split(['\r', '\n'], 2)[0];
        var connectionString = DamengTestEnvironment.ConnectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            firstLine = firstLine.Replace(connectionString, "[redacted]", StringComparison.OrdinalIgnoreCase);
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (string key in builder.Keys)
            {
                if (!key.Contains("password", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("pwd", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var secret = Convert.ToString(builder[key], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(secret))
                {
                    firstLine = firstLine.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return firstLine.Length > 160 ? firstLine[..160] : firstLine;
    }

    private sealed record ProbeCase(string Id, string Sql);
}
