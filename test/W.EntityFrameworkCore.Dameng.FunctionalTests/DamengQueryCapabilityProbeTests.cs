using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// 候选 SQL 的服务器语义测量。测试通过只表示所有候选均已尝试，不表示翻译器可用。
/// 每个 case 的独立 JSON 行包含成功值或 SQL 错误，不能作为功能验收断言。
/// </summary>
public sealed class DamengQueryCapabilityProbeTests(ITestOutputHelper output)
{
    private const int CommandTimeoutSeconds = 15;

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task EnvironmentMetadata()
        => ProbeAsync(
        [
            new("ENV.banner", "SELECT BANNER FROM V$VERSION"),
            new("ENV.svr_version", "SELECT SVR_VERSION() FROM dual"),
            new("ENV.page", "SELECT PAGE() FROM dual"),
            new("ENV.compatible_mode", "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'COMPATIBLE_MODE'"),
            new("ENV.length_in_char", "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'LENGTH_IN_CHAR'"),
            new("ENV.global_charset", "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'GLOBAL_CHARSET'"),
            new("ENV.calc_as_decimal", "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'CALC_AS_DECIMAL'"),
            new("ENV.json_mode", "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'JSON_MODE'")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task Q1NumberFunctions()
        => ProbeAsync(
        [
            new("Q1.abs_int_negative", "SELECT ABS(CAST(-42 AS INT)) FROM dual"),
            new("Q1.abs_long_negative", "SELECT ABS(CAST(-42 AS BIGINT)) FROM dual"),
            new("Q1.abs_decimal", "SELECT ABS(CAST(-12.375 AS DECIMAL(18,3))) FROM dual"),
            new("Q1.abs_double", "SELECT ABS(CAST(-12.375 AS DOUBLE)) FROM dual"),
            new("Q1.abs_int_min", "SELECT ABS(CAST(-2147483647 - 1 AS INT)) FROM dual"),
            new("Q1.abs_long_min", "SELECT ABS(CAST(-9223372036854775807 - 1 AS BIGINT)) FROM dual"),
            new("Q1.sign_negative", "SELECT SIGN(CAST(-12.375 AS DECIMAL(18,3))) FROM dual"),
            new("Q1.sign_zero", "SELECT SIGN(CAST(0 AS DOUBLE)) FROM dual"),
            new("Q1.sign_positive", "SELECT SIGN(CAST(12.375 AS DOUBLE)) FROM dual"),
            new("Q1.floor_decimal", "SELECT FLOOR(CAST(-12.375 AS DECIMAL(18,3))) FROM dual"),
            new("Q1.floor_double", "SELECT FLOOR(CAST(12.375 AS DOUBLE)) FROM dual"),
            new("Q1.ceil_decimal", "SELECT CEIL(CAST(-12.375 AS DECIMAL(18,3))) FROM dual"),
            new("Q1.ceil_double", "SELECT CEIL(CAST(12.375 AS DOUBLE)) FROM dual"),
            new("Q1.null", "SELECT ABS(CAST(NULL AS INT)) FROM dual")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task Q1NonFiniteDoubleParameterCapability()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var inputs = new (string Name, double Value)[]
        {
            ("nan", double.NaN),
            ("positive_infinity", double.PositiveInfinity),
            ("negative_infinity", double.NegativeInfinity)
        };
        var expressions = new (string Name, string Sql)[]
        {
            ("direct", "SELECT :p FROM dual"),
            ("abs", "SELECT ABS(:p) FROM dual"),
            ("sign", "SELECT SIGN(:p) FROM dual"),
            ("floor", "SELECT FLOOR(:p) FROM dual"),
            ("ceil", "SELECT CEIL(:p) FROM dual")
        };

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            foreach (var expression in expressions)
            {
                var status = await ProbeNonFiniteDoubleAsync(
                    connection,
                    $"Q1.nonfinite.{input.Name}.{expression.Name}",
                    expression.Sql,
                    input.Value);
                counts[status] = counts.GetValueOrDefault(status) + 1;
            }
        }

        Write(new { Status = "summary", Category = "Q1.nonfinite", Cases = 15, Counts = counts });
        Assert.Equal(15, counts.Values.Sum());
    }

    private async Task<string> ProbeNonFiniteDoubleAsync(
        DbConnection connection,
        string id,
        string sql,
        double input)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        var watch = Stopwatch.StartNew();
        var phase = "bind";
        try
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p";
            parameter.DbType = DbType.Double;
            parameter.Value = input;
            command.Parameters.Add(parameter);

            phase = "execute";
            await using var reader = await command.ExecuteReaderAsync();
            phase = "fetch";
            if (!await reader.ReadAsync())
            {
                Write(new { Id = id, Status = "no_row", ElapsedMs = watch.ElapsedMilliseconds });
                return "no_row";
            }

            phase = "read";
            var value = reader.GetValue(0);
            Write(new
            {
                Id = id,
                Status = "value",
                Value = FormatValue(value),
                ClrType = value is DBNull ? null : value.GetType().FullName,
                FieldType = reader.GetFieldType(0).FullName,
                DataType = reader.GetDataTypeName(0),
                ElapsedMs = watch.ElapsedMilliseconds
            });
            return "value";
        }
        catch (Exception error) when (error is DbException or ArgumentException or InvalidOperationException
            or InvalidCastException or OverflowException or FormatException or NotSupportedException)
        {
            var status = $"{phase}_error";
            Write(new
            {
                Id = id,
                Status = status,
                ErrorType = error.GetType().Name,
                ErrorCode = (error as DbException)?.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
            return status;
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task Q2ExponentLogarithmAndRoot()
        => ProbeAsync(
        [
            new("Q2.exp", "SELECT EXP(CAST(1 AS DOUBLE)) FROM dual"),
            new("Q2.exp_overflow", "SELECT EXP(CAST(1000 AS DOUBLE)) FROM dual"),
            new("Q2.ln", "SELECT LN(CAST(2 AS DOUBLE)) FROM dual"),
            new("Q2.ln_zero", "SELECT LN(CAST(0 AS DOUBLE)) FROM dual"),
            new("Q2.ln_negative", "SELECT LN(CAST(-1 AS DOUBLE)) FROM dual"),
            new("Q2.log_100_10", "SELECT LOG(CAST(100 AS DOUBLE), CAST(10 AS DOUBLE)) FROM dual"),
            new("Q2.log_10_100", "SELECT LOG(CAST(10 AS DOUBLE), CAST(100 AS DOUBLE)) FROM dual"),
            new("Q2.log_value_one", "SELECT LOG(CAST(100 AS DOUBLE), CAST(1 AS DOUBLE)) FROM dual"),
            new("Q2.log_base_one", "SELECT LOG(CAST(1 AS DOUBLE), CAST(100 AS DOUBLE)) FROM dual"),
            new("Q2.log_base_zero", "SELECT LOG(CAST(0 AS DOUBLE), CAST(100 AS DOUBLE)) FROM dual"),
            new("Q2.log_base_negative", "SELECT LOG(CAST(-2 AS DOUBLE), CAST(100 AS DOUBLE)) FROM dual"),
            new("Q2.log10", "SELECT LOG10(CAST(100 AS DOUBLE)) FROM dual"),
            new("Q2.power", "SELECT POWER(CAST(2 AS DOUBLE), CAST(3 AS DOUBLE)) FROM dual"),
            new("Q2.power_fraction", "SELECT POWER(CAST(-2 AS DOUBLE), CAST(0.5 AS DOUBLE)) FROM dual"),
            new("Q2.sqrt", "SELECT SQRT(CAST(2 AS DOUBLE)) FROM dual"),
            new("Q2.sqrt_negative", "SELECT SQRT(CAST(-1 AS DOUBLE)) FROM dual")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task Q3TrimCharacterSetAndEmptyString()
        => ProbeAsync(
        [
            new("Q3.left_alternating", "SELECT LTRIM('xyxy正文yx', 'xy') FROM dual"),
            new("Q3.right_alternating", "SELECT RTRIM('xy正文yxyx', 'xy') FROM dual"),
            new("Q3.both_alternating", "SELECT RTRIM(LTRIM('xy正文yxy', 'xy'), 'xy') FROM dual"),
            new("Q3.middle_unchanged", "SELECT LTRIM('xy正xy文', 'xy') FROM dual"),
            new("Q3.chinese_set", "SELECT LTRIM('甲乙甲正文', '甲乙') FROM dual"),
            new("Q3.tab", "SELECT LTRIM(CHR(9) || CHR(9) || '正文', CHR(9)) FROM dual"),
            new("Q3.quote", "SELECT LTRIM('''正文', '''') FROM dual"),
            new("Q3.all_removed", "SELECT LTRIM('xyxy', 'xy') FROM dual"),
            new("Q3.empty_input", "SELECT LTRIM('', 'x') FROM dual"),
            new("Q3.empty_set", "SELECT LTRIM('x正文', '') FROM dual")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task Q0NclobConcatDiagnostics()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_Q0_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT, \"SHORT_TEXT\" NVARCHAR2(64), \"LONG_TEXT\" NCLOB, \"OTHER\" NCLOB)");
            created = true;

            await InsertQ0RowAsync(connection, tableName, 1, null, null, null);
            await InsertQ0RowAsync(connection, tableName, 2, string.Empty, string.Empty, string.Empty);
            await InsertQ0RowAsync(connection, tableName, 3, "   ", "   ", "   ");
            await InsertQ0RowAsync(connection, tableName, 4, "中文", new string('中', 20_000), "尾");

            foreach (var id in new[] { 1, 2, 3 })
            {
                var source = $"FROM \"{tableName}\" WHERE \"ID\" = {id}";
                await ExecuteCaseAsync(connection, new ProbeCase($"Q0.short.length_{id}", $"SELECT LENGTH(\"SHORT_TEXT\") {source}"));
                await ExecuteCaseAsync(connection, new ProbeCase($"Q0.nclob.length_{id}", $"SELECT LENGTH(\"LONG_TEXT\") {source}"));
                await ProbeTextCaseAsync(connection, $"Q0.short.value_{id}", $"SELECT \"SHORT_TEXT\" {source}");
                await ProbeTextCaseAsync(connection, $"Q0.nclob.value_{id}", $"SELECT \"LONG_TEXT\" {source}");
            }

            var expressions = new (string Name, string Sql)[]
            {
                ("original_column", "\"LONG_TEXT\""),
                ("column_concat", "\"LONG_TEXT\" || \"OTHER\""),
                ("coalesce_varchar_concat", "COALESCE(\"LONG_TEXT\", '') || COALESCE(\"OTHER\", '')"),
                ("coalesce_nclob_concat", "COALESCE(\"LONG_TEXT\", CAST('' AS NCLOB)) || COALESCE(\"OTHER\", CAST('' AS NCLOB))"),
                ("cast_coalesce_concat", "CAST(COALESCE(\"LONG_TEXT\", '') AS NCLOB) || CAST(COALESCE(\"OTHER\", '') AS NCLOB)")
            };
            foreach (var expression in expressions)
            {
                var source = $"FROM \"{tableName}\" WHERE \"ID\" = 4";
                await ExecuteCaseAsync(
                    connection,
                    new ProbeCase($"Q0.{expression.Name}.server_length", $"SELECT LENGTH({expression.Sql}) {source}"));
                await ProbeTextCaseAsync(
                    connection,
                    $"Q0.{expression.Name}.value",
                    $"SELECT {expression.Sql} {source}");
            }
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    private static async Task InsertQ0RowAsync(
        DbConnection connection,
        string tableName,
        int id,
        string? shortText,
        string? longText,
        string? other)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO \"{tableName}\" (\"ID\", \"SHORT_TEXT\", \"LONG_TEXT\", \"OTHER\") VALUES (:id, :short_text, :long_text, :other)";
        command.CommandTimeout = CommandTimeoutSeconds;
        AddParameter(command, "id", id);
        AddTextParameter(command, "short_text", shortText, isLob: false);
        AddTextParameter(command, "long_text", longText, isLob: true);
        AddTextParameter(command, "other", other, isLob: true);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddTextParameter(DbCommand command, string name, string? value, bool isLob)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Size = Math.Max(1, value?.Length ?? 0);
        parameter.Value = value is null ? DBNull.Value : value;
        if (isLob && parameter is DmParameter dmParameter)
        {
            dmParameter.DmSqlType = DmDbType.Clob;
        }

        command.Parameters.Add(parameter);
    }

    private async Task ProbeTextCaseAsync(DbConnection connection, string id, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        var watch = Stopwatch.StartNew();
        var phase = "execute";
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            phase = "fetch";
            if (!await reader.ReadAsync())
            {
                Write(new { Id = id, Status = "no_row", ElapsedMs = watch.ElapsedMilliseconds });
                return;
            }

            phase = "metadata";
            var dataType = reader.GetDataTypeName(0);
            var fieldType = reader.GetFieldType(0).FullName;
            string? getStringError = null;
            string? getValueError = null;
            string? directText = null;
            object? value = null;
            try
            {
                phase = "get_string";
                directText = reader.IsDBNull(0) ? null : reader.GetString(0);
            }
            catch (Exception error) when (error is DbException or InvalidCastException or NotSupportedException)
            {
                getStringError = $"{error.GetType().Name}: {SafeSummary(error.Message)}";
            }

            try
            {
                phase = "get_value";
                value = reader.GetValue(0);
            }
            catch (Exception error) when (error is DbException or InvalidCastException or NotSupportedException)
            {
                getValueError = $"{error.GetType().Name}: {SafeSummary(error.Message)}";
            }

            var valueText = value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            Write(new
            {
                Id = id,
                Status = "read_attempted",
                DataType = dataType,
                FieldType = fieldType,
                GetStringLength = directText?.Length,
                GetStringSuffix = directText is { Length: > 8 } ? directText[^8..] : directText,
                GetStringError = getStringError,
                ValueType = value is null or DBNull ? null : value.GetType().FullName,
                GetValueLength = valueText?.Length,
                GetValueSuffix = valueText is { Length: > 8 } ? valueText[^8..] : valueText,
                GetValueError = getValueError,
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
        catch (DbException error)
        {
            Write(new
            {
                Id = id,
                Status = "sql_error",
                Phase = phase,
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task Q4JsonValueContract()
        => ProbeAsync(
        [
            new("Q4.json_mode", "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME = 'JSON_MODE'"),
            new("Q4.string_default", """SELECT JSON_VALUE('{"a":"文字"}', '$.a') FROM dual"""),
            new("Q4.string_returning", """SELECT JSON_VALUE('{"a":"文字"}', '$.a' RETURNING VARCHAR2(4000)) FROM dual"""),
            new("Q4.int_returning", """SELECT JSON_VALUE('{"a":42}', '$.a' RETURNING INT) FROM dual"""),
            new("Q4.nested", """SELECT JSON_VALUE('{"a":{"b":"nested"}}', '$.a.b') FROM dual"""),
            new("Q4.missing", """SELECT JSON_VALUE('{"a":1}', '$.missing') FROM dual"""),
            new("Q4.json_null", """SELECT JSON_VALUE('{"a":null}', '$.a') FROM dual"""),
            new("Q4.object_as_scalar", """SELECT JSON_VALUE('{"a":{"b":1}}', '$.a') FROM dual"""),
            new("Q4.array_as_scalar", """SELECT JSON_VALUE('{"a":[1]}', '$.a') FROM dual"""),
            new("Q4.type_error", """SELECT JSON_VALUE('{"a":"text"}', '$.a' RETURNING INT) FROM dual"""),
            new("Q4.integer_overflow", """SELECT JSON_VALUE('{"a":2147483648}', '$.a' RETURNING INT) FROM dual"""),
            new("Q4.invalid_document", "SELECT JSON_VALUE('{broken', '$.a') FROM dual"),
            new("Q4.on_error", """SELECT JSON_VALUE('{"a":"text"}', '$.a' RETURNING INT ERROR ON ERROR) FROM dual"""),
            new("Q4.quoted_property", """SELECT JSON_VALUE('{"a.b":1}', '$."a.b"') FROM dual"""),
            new("Q4.chinese_property", """SELECT JSON_VALUE('{"中文":1}', '$."中文"') FROM dual"""),
            new("Q4.returning_small_overflow", """SELECT JSON_VALUE('{"a":"abcdefghijklmnop"}', '$.a' RETURNING VARCHAR2(4) ERROR ON ERROR) FROM dual"""),
            new("Q4.returning_missing_error_on_empty", """SELECT JSON_VALUE('{"a":1}', '$.missing' RETURNING VARCHAR2(4000) ERROR ON EMPTY ERROR ON ERROR) FROM dual"""),
            new("Q4.returning_missing_null_on_empty", """SELECT JSON_VALUE('{"a":1}', '$.missing' RETURNING VARCHAR2(4000) NULL ON EMPTY ERROR ON ERROR) FROM dual"""),
            new("Q4.policy_json_null", """SELECT JSON_VALUE('{"a":null}', '$.a' RETURNING VARCHAR2(4000) NULL ON EMPTY ERROR ON ERROR) FROM dual"""),
            new("Q4.policy_object_error", """SELECT JSON_VALUE('{"a":{"b":1}}', '$.a' RETURNING VARCHAR2(4000) NULL ON EMPTY ERROR ON ERROR) FROM dual"""),
            new("Q4.policy_array_error", """SELECT JSON_VALUE('{"a":[1]}', '$.a' RETURNING VARCHAR2(4000) NULL ON EMPTY ERROR ON ERROR) FROM dual"""),
            new("Q4.policy_type_error", """SELECT JSON_VALUE('{"a":"text"}', '$.a' RETURNING INT NULL ON EMPTY ERROR ON ERROR) FROM dual"""),
            new("Q4.returning_long", "SELECT JSON_VALUE('{\"a\":\"' || RPAD('x', 4001, 'x') || '\"}', '$.a' RETURNING VARCHAR2(8000) ERROR ON ERROR) FROM dual")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task Q5DateBucketsAndQuarter()
        => ProbeAsync(
        [
            new("Q5.hour", "SELECT TRUNC(CAST('2026-12-31 23:59:59.1234567' AS DATETIME(7)), 'HH24') FROM dual"),
            new("Q5.minute", "SELECT TRUNC(CAST('2026-12-31 23:59:59.1234567' AS DATETIME(7)), 'MI') FROM dual"),
            new("Q5.second", "SELECT TRUNC(CAST('2026-12-31 23:59:59.1234567' AS DATETIME(7)), 'SS') FROM dual"),
            new("Q5.iso_week_sunday", "SELECT TRUNC(DATETIME '2027-01-03 23:59:59', 'IW') FROM dual"),
            new("Q5.iso_week_monday", "SELECT TRUNC(DATETIME '2027-01-04 00:00:00', 'IW') FROM dual"),
            new("Q5.leap_day", "SELECT TRUNC(DATETIME '2028-02-29 13:14:15', 'IW') FROM dual"),
            new("Q5.quarter_datepart", "SELECT DATEPART(quarter, DATETIME '2026-12-31 23:59:59') FROM dual"),
            new("Q5.quarter_month_arithmetic", "SELECT FLOOR((DATEPART(month, DATETIME '2026-12-31 23:59:59') - 1) / 3.0) + 1 FROM dual")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task S1DateTimeOffsetStorageAndMembers()
    {
        await ProbeAsync(
        [
            new("S1.session_timezone", "SELECT SESSIONTIMEZONE FROM dual"),
            new("S1.current_timestamp", "SELECT CURRENT_TIMESTAMP FROM dual", ReadDirectString: true),
            new("S1.offset_plus_eight", "SELECT CAST('2026-07-23 14:15:16.1234567 +08:00' AS DATETIME(7) WITH TIME ZONE) FROM dual", ReadDirectString: true),
            new("S1.offset_minus_five", "SELECT CAST('2026-07-23 01:15:16.1234567 -05:00' AS DATETIME(7) WITH TIME ZONE) FROM dual", ReadDirectString: true),
            new("S1.offset_plus_five_thirty", "SELECT CAST('2026-07-23 11:45:16.1234567 +05:30' AS DATETIME(7) WITH TIME ZONE) FROM dual", ReadDirectString: true),
            new("S1.offset_utc", "SELECT CAST('2026-07-23 06:15:16.1234567 +00:00' AS DATETIME(7) WITH TIME ZONE) FROM dual", ReadDirectString: true),
            new("S1.cross_day", "SELECT CAST('2027-01-01 05:00:00.0000001 +05:30' AS DATETIME(7) WITH TIME ZONE) FROM dual", ReadDirectString: true),
            new("S1.original_hour", "SELECT DATEPART(hour, CAST('2027-01-01 05:00:00.0000001 +05:30' AS DATETIME(7) WITH TIME ZONE)) FROM dual"),
            new("S1.original_day", "SELECT DATEPART(day, CAST('2027-01-01 05:00:00.0000001 +05:30' AS DATETIME(7) WITH TIME ZONE)) FROM dual"),
            new("S1.utc_conversion", "SELECT SYS_EXTRACT_UTC(CAST('2027-01-01 05:00:00.0000001 +05:30' AS DATETIME(7) WITH TIME ZONE)) FROM dual", ReadDirectString: true),
            new("S1.server_text_precision", "SELECT TO_CHAR(CAST('2027-01-01 05:00:00.0000001 +05:30' AS DATETIME(7) WITH TIME ZONE), 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM') FROM dual"),
            new("S1.utc_at_timezone", "SELECT CAST('2027-01-01 05:00:00.0000001 +05:30' AS DATETIME(7) WITH TIME ZONE) AT TIME ZONE 'UTC' FROM dual", ReadDirectString: true)
        ]);
        await ProbeStoredOffsetsAsync();
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task S2ListaggNullOrderAndOverflow()
        => ProbeAsync(
        [
            new("S2.ordered", "SELECT LISTAGG(v, ',') WITHIN GROUP (ORDER BY ord) FROM (SELECT 2 ord, 'B' v FROM dual UNION ALL SELECT 1, 'A' FROM dual UNION ALL SELECT 3, 'B' FROM dual)"),
            new("S2.null_element", "SELECT LISTAGG(v, ',') WITHIN GROUP (ORDER BY ord) FROM (SELECT 1 ord, 'A' v FROM dual UNION ALL SELECT 2, CAST(NULL AS VARCHAR2(10)) FROM dual UNION ALL SELECT 3, 'B' FROM dual)"),
            new("S2.all_null", "SELECT LISTAGG(v, ',') WITHIN GROUP (ORDER BY ord) FROM (SELECT 1 ord, CAST(NULL AS VARCHAR2(10)) v FROM dual UNION ALL SELECT 2, CAST(NULL AS VARCHAR2(10)) FROM dual)"),
            new("S2.empty_element", "SELECT LISTAGG(v, ',') WITHIN GROUP (ORDER BY ord) FROM (SELECT 1 ord, 'A' v FROM dual UNION ALL SELECT 2, '' FROM dual UNION ALL SELECT 3, 'B' FROM dual)"),
            new("S2.empty_separator", "SELECT LISTAGG(v, '') WITHIN GROUP (ORDER BY ord) FROM (SELECT 2 ord, 'B' v FROM dual UNION ALL SELECT 1, 'A' FROM dual)"),
            new("S2.empty_group", "SELECT LISTAGG(v, ',') WITHIN GROUP (ORDER BY ord) FROM (SELECT 1 ord, 'A' v FROM dual WHERE 1 = 0)"),
            new("S2.overflow", "SELECT LISTAGG(RPAD('x', 200, 'x'), ',') WITHIN GROUP (ORDER BY LEVEL) FROM dual CONNECT BY LEVEL <= 200")
        ]);

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task S3ParameterizedInBoundaries()
    {
        var cases = new List<ProbeCase>
        {
            new("S3.empty", "SELECT COUNT(*) FROM (SELECT 1 v FROM dual) WHERE 1 = 0")
        };

        foreach (var size in new[] { 1, 499, 500, 501, 998, 999, 1000, 2000 })
        {
            cases.Add(CreateInCase($"S3.single_{size}", size));
        }

        cases.Add(CreateInCase("S3.duplicates_5", 5, duplicates: true));
        cases.Add(CreateInCase("S3.null_5", 5, includeNull: true));
        cases.Add(CreateInCase("S3.negated_501", 501, negated: true));
        cases.Add(CreateTwoInCase("S3.two_in_499_501_plus_filter", 499, 501));
        cases.Add(CreateTwoInCase("S3.two_in_999_1000_plus_filter", 999, 1000));
        return ProbeAsync(cases);
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task S3EfContainsExpansion()
    {
        var commands = new SetCommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<SetProbeContext>()
            .UseDameng(DamengTestEnvironment.GetRequiredConnectionString())
            .AddInterceptors(commands)
            .Options;
        await using var context = new SetProbeContext(options);
        context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        // 连接故障在 case 捕获范围外，不能被误报为某个 SQL 候选失败。
        await context.Database.OpenConnectionAsync();

        await ProbeEfCaseAsync(
            context,
            commands,
            "S3.EF.constant",
            db => SetSource(db)
                .Where(row => new int?[] { 0, 1 }.Contains(row.Id))
                .Select(row => row.Id));

        foreach (var size in new[] { 0, 1, 499, 500, 501, 998, 999, 1000, 2000 })
        {
            var values = Enumerable.Range(0, size).Select(index => (int?)index).ToArray();
            await ProbeEfCaseAsync(
                context,
                commands,
                $"S3.EF.parameter_{size}",
                db => SetSource(db)
                    .Where(row => values.Contains(row.Id))
                    .Select(row => row.Id));
        }

        int?[] nullableValues = [null, 1];
        await ProbeEfCaseAsync(
            context,
            commands,
            "S3.EF.null",
            db => SetSource(db)
                .Where(row => nullableValues.Contains(row.Id))
                .Select(row => row.Id));
        await ProbeEfCaseAsync(
            context,
            commands,
            "S3.EF.negated_null",
            db => SetSource(db)
                .Where(row => !nullableValues.Contains(row.Id))
                .Select(row => row.Id));

        int?[] first = Enumerable.Range(0, 499).Select(index => (int?)index).ToArray();
        int?[] second = Enumerable.Range(499, 501).Select(index => (int?)index).ToArray();
        int? filter = 1;
        await ProbeEfCaseAsync(
            context,
            commands,
            "S3.EF.two_sets_plus_filter",
            db => SetSource(db)
                .Where(row => (first.Contains(row.Id) || second.Contains(row.Id)) && row.Id == filter)
                .Select(row => row.Id));

        int?[] pageValues = [0, 1];
        await ProbeEfCaseAsync(
            context,
            commands,
            "S3.EF.order_page",
            db => SetSource(db)
                .Where(row => pageValues.Contains(row.Id))
                .OrderBy(row => row.Id)
                .Skip(1)
                .Take(1)
                .Select(row => row.Id));
    }

    private async Task ProbeEfCaseAsync(
        SetProbeContext context,
        SetCommandCaptureInterceptor commands,
        string id,
        Func<SetProbeContext, IQueryable<int?>> createQuery)
    {
        commands.Commands.Clear();
        string? generatedSql = null;
        var watch = Stopwatch.StartNew();
        try
        {
            var query = createQuery(context);
            generatedSql = query.ToQueryString();
            var values = await query.ToListAsync();
            Write(new
            {
                Id = id,
                Status = "value",
                Results = values.Select(value => value?.ToString(CultureInfo.InvariantCulture) ?? "null"),
                GeneratedSql = generatedSql,
                ActualCommands = commands.Commands,
                CommandCount = commands.Commands.Count,
                TotalParameters = commands.Commands.Sum(command => command.Parameters),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
        catch (Exception error) when (error is DbException or InvalidOperationException or NotSupportedException)
        {
            Write(new
            {
                Id = id,
                Status = error is DbException ? "sql_error" : "translation_error",
                ErrorType = error.GetType().Name,
                ErrorCode = (error as DbException)?.ErrorCode,
                Summary = SafeSummary(error.Message),
                GeneratedSql = generatedSql,
                ActualCommands = commands.Commands,
                CommandCount = commands.Commands.Count,
                TotalParameters = commands.Commands.Sum(command => command.Parameters),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
    }

    private static IQueryable<SetProbeRow> SetSource(SetProbeContext context)
        => context.Rows.FromSqlRaw(
            "SELECT CAST(0 AS INT) AS \"ID\" FROM dual "
            + "UNION ALL SELECT CAST(1 AS INT) AS \"ID\" FROM dual "
            + "UNION ALL SELECT CAST(NULL AS INT) AS \"ID\" FROM dual");

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public Task S4FractionalDateArithmetic()
        => ProbeAsync(
        [
            new("S4.dateadd_half_second_literal", "SELECT DATEADD(second, 0.5, CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7))) FROM dual"),
            new("S4.dateadd_half_second_parameter", "SELECT DATEADD(second, :delta, CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7))) FROM dual", BindDecimal("delta", 0.5m)),
            new("S4.dateadd_minus_quarter_parameter", "SELECT DATEADD(second, :delta, CAST('2028-02-29 00:00:00.1234567' AS DATETIME(7))) FROM dual", BindDecimal("delta", -0.25m)),
            new("S4.interval_half_second_literal", "SELECT CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)) + NUMTODSINTERVAL(0.5, 'SECOND') FROM dual"),
            new("S4.interval_half_second_parameter", "SELECT CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)) + NUMTODSINTERVAL(:delta, 'SECOND') FROM dual", BindDecimal("delta", 0.5m)),
            new("S4.interval_tick_parameter", "SELECT CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)) + NUMTODSINTERVAL(:delta, 'SECOND') FROM dual", BindDecimal("delta", 0.0000001m)),
            new("S4.day_fraction_literal", "SELECT CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)) + 0.5 / 86400.0 FROM dual"),
            new("S4.day_fraction_parameter", "SELECT CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)) + :delta / 86400.0 FROM dual", BindDecimal("delta", 0.5m)),
            new("S4.day_negative_parameter", "SELECT CAST('2028-02-29 00:00:00.1234567' AS DATETIME(7)) + :delta / 86400.0 FROM dual", BindDecimal("delta", -0.25m)),
            new("S4.server_text_precision", "SELECT TO_CHAR(CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)), 'YYYY-MM-DD HH24:MI:SS.FF7') FROM dual"),
            new("S4.server_text_tick_added", "SELECT TO_CHAR(CAST('2028-02-29 23:59:59.1234567' AS DATETIME(7)) + NUMTODSINTERVAL(0.0000001, 'SECOND'), 'YYYY-MM-DD HH24:MI:SS.FF7') FROM dual")
        ]);

    private async Task ProbeStoredOffsetsAsync()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_QP_{suffix}";
        var samples = new (int Id, string Name, string Literal)[]
        {
            (1, "plus_eight", "2026-07-23 14:15:16.1234567 +08:00"),
            (2, "minus_five", "2026-07-23 01:15:16.1234567 -05:00"),
            (3, "plus_five_thirty", "2026-07-23 11:45:16.1234567 +05:30"),
            (4, "utc", "2026-07-23 06:15:16.1234567 +00:00"),
            (5, "cross_day", "2027-01-01 05:00:00.0000001 +05:30")
        };

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT, \"VALUE\" DATETIME(7) WITH TIME ZONE)");
            created = true;

            foreach (var sample in samples)
            {
                var insertSql = $"INSERT INTO \"{tableName}\" (\"ID\", \"VALUE\") VALUES ({sample.Id}, CAST('{sample.Literal}' AS DATETIME(7) WITH TIME ZONE))";
                try
                {
                    await ExecuteNonQueryAsync(connection, insertSql);
                    Write(new { Id = $"S1.store.{sample.Name}.insert", Status = "inserted" });
                }
                catch (DbException error)
                {
                    Write(new
                    {
                        Id = $"S1.store.{sample.Name}.insert",
                        Status = "sql_error",
                        error.ErrorCode,
                        Summary = SafeSummary(error.Message)
                    });
                    continue;
                }

                var source = $"FROM \"{tableName}\" WHERE \"ID\" = {sample.Id}";
                await ExecuteCaseAsync(connection, new ProbeCase($"S1.store.{sample.Name}.raw", $"SELECT \"VALUE\" {source}", ReadDirectString: true));
                await ExecuteCaseAsync(connection, new ProbeCase($"S1.store.{sample.Name}.text", $"SELECT CAST(\"VALUE\" AS VARCHAR2(100)) {source}"));
                await ExecuteCaseAsync(connection, new ProbeCase($"S1.store.{sample.Name}.server_text_precision", $"SELECT TO_CHAR(\"VALUE\", 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM') {source}"));
                await ExecuteCaseAsync(connection, new ProbeCase($"S1.store.{sample.Name}.hour", $"SELECT DATEPART(hour, \"VALUE\") {source}"));
                await ExecuteCaseAsync(connection, new ProbeCase($"S1.store.{sample.Name}.day", $"SELECT DATEPART(day, \"VALUE\") {source}"));
                await ExecuteCaseAsync(connection, new ProbeCase($"S1.store.{sample.Name}.utc", $"SELECT SYS_EXTRACT_UTC(\"VALUE\") {source}", ReadDirectString: true));
            }
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync();
    }

    private static ProbeCase CreateInCase(
        string id,
        int size,
        bool duplicates = false,
        bool includeNull = false,
        bool negated = false)
    {
        var names = Enumerable.Range(0, size).Select(index => $":p{index}");
        var operation = negated ? "NOT IN" : "IN";
        var sql = $"SELECT COUNT(*) FROM (SELECT 1 v FROM dual) WHERE v {operation} ({string.Join(",", names)})";
        return new ProbeCase(id, sql, command =>
        {
            for (var index = 0; index < size; index++)
            {
                AddParameter(
                    command,
                    $"p{index}",
                    includeNull && index == size - 1
                        ? DBNull.Value
                        : duplicates ? 1 : index);
            }
        });
    }

    private static ProbeCase CreateTwoInCase(string id, int firstSize, int secondSize)
    {
        var first = string.Join(",", Enumerable.Range(0, firstSize).Select(index => $":p{index}"));
        var second = string.Join(",", Enumerable.Range(firstSize, secondSize).Select(index => $":p{index}"));
        var sql = $"SELECT COUNT(*) FROM (SELECT 1 v FROM dual) WHERE (v IN ({first}) OR v IN ({second})) AND v = :filter";
        return new ProbeCase(id, sql, command =>
        {
            for (var index = 0; index < firstSize + secondSize; index++)
            {
                AddParameter(command, $"p{index}", index);
            }

            AddParameter(command, "filter", 1);
        });
    }

    private static Action<DbCommand> BindDecimal(string name, decimal value)
        => command => AddParameter(command, name, value, DbType.Decimal);

    private static void AddParameter(DbCommand command, string name, object value, DbType? type = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        parameter.DbType = type ?? DbType.Int32;
        command.Parameters.Add(parameter);
    }

    private async Task ProbeAsync(IReadOnlyList<ProbeCase> cases)
    {
        // 连接问题要使整个测试失败；SQL 候选不支持则留在各 case 的证据中。
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var probe in cases)
        {
            var status = await ExecuteCaseAsync(connection, probe);
            counts[status] = counts.GetValueOrDefault(status) + 1;
        }

        Write(new { Status = "summary", Cases = cases.Count, Counts = counts });
        Assert.Equal(cases.Count, counts.Values.Sum());
    }

    private async Task<string> ExecuteCaseAsync(DbConnection connection, ProbeCase probe)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = probe.Sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        probe.Bind?.Invoke(command);
        var watch = Stopwatch.StartNew();

        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                Write(new { probe.Id, Status = "no_row", Parameters = command.Parameters.Count, SqlLength = probe.Sql.Length, ElapsedMs = watch.ElapsedMilliseconds });
                return "no_row";
            }

            string? directString = null;
            string? directStringError = null;
            if (probe.ReadDirectString)
            {
                try
                {
                    directString = reader.GetString(0);
                }
                catch (Exception error) when (error is DbException or InvalidCastException or NotSupportedException)
                {
                    directStringError = error.GetType().Name;
                }
            }

            var value = reader.GetValue(0);
            var display = FormatValue(value);
            Write(new
            {
                probe.Id,
                Status = "value",
                Value = display is { Length: > 160 } ? display[..160] : display,
                ValueLength = display?.Length,
                ClrType = value is DBNull ? null : value.GetType().FullName,
                DirectString = directString,
                DirectStringError = directStringError,
                FieldType = reader.GetFieldType(0).FullName,
                DataType = reader.GetDataTypeName(0),
                Parameters = command.Parameters.Count,
                SqlLength = probe.Sql.Length,
                ElapsedMs = watch.ElapsedMilliseconds
            });
            return "value";
        }
        catch (Exception error) when (error is DbException or InvalidCastException or FormatException or OverflowException or NotSupportedException)
        {
            Write(new
            {
                probe.Id,
                Status = error is DbException ? "sql_error" : "read_error",
                ErrorType = error.GetType().Name,
                ErrorCode = (error as DbException)?.ErrorCode,
                Summary = SafeSummary(error.Message),
                Parameters = command.Parameters.Count,
                SqlLength = probe.Sql.Length,
                ElapsedMs = watch.ElapsedMilliseconds
            });
            return error is DbException ? "sql_error" : "read_error";
        }
    }

    private void Write<T>(T result)
        => output.WriteLine(JsonSerializer.Serialize(result));

    private static string? FormatValue(object value)
        => value switch
        {
            DBNull => null,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

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
                if (!IsSensitiveConnectionField(key))
                {
                    continue;
                }

                var value = Convert.ToString(builder[key], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(value))
                {
                    firstLine = firstLine.Replace(value, "[redacted]", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return firstLine.Length > 160 ? firstLine[..160] : firstLine;
    }

    private static bool IsSensitiveConnectionField(string key)
        => key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Equals("pwd", StringComparison.OrdinalIgnoreCase)
            || key.Contains("user", StringComparison.OrdinalIgnoreCase)
            || key.Equals("uid", StringComparison.OrdinalIgnoreCase)
            || key.Contains("server", StringComparison.OrdinalIgnoreCase)
            || key.Contains("host", StringComparison.OrdinalIgnoreCase)
            || key.Contains("data source", StringComparison.OrdinalIgnoreCase);

    private sealed class SetProbeContext(DbContextOptions<SetProbeContext> options) : DbContext(options)
    {
        public DbSet<SetProbeRow> Rows => Set<SetProbeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SetProbeRow>(entity =>
            {
                entity.HasNoKey();
                entity.Property(row => row.Id).HasColumnName("ID");
            });
    }

    private sealed class SetProbeRow
    {
        public int? Id { get; set; }
    }

    private sealed class SetCommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<SetCommandEvidence> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(new SetCommandEvidence(command.CommandText, command.Parameters.Count));
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new SetCommandEvidence(command.CommandText, command.Parameters.Count));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record SetCommandEvidence(string Sql, int Parameters);

    private sealed record ProbeCase(
        string Id,
        string Sql,
        Action<DbCommand>? Bind = null,
        bool ReadDirectString = false);
}
