using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Dm;
using W.EntityFrameworkCore.Dameng.TestData;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// S2/Q7 的 LISTAGG 黑盒探针。候选执行成功不代表 string.Join/Concat 已获得支持。
/// 尤其测量 LISTAGG 跳过空串时，COALESCE(value, '') 是否仍会丢失元素。
/// </summary>
public sealed class DamengStringAggregateCapabilityProbeTests(ITestOutputHelper output)
{
    private const int CommandTimeoutSeconds = 15;

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task OrderedNullEmptyUnicodeAndOverflow()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var tableName = "EF10_AG_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var quotedTable = $"\"{tableName}\"";
        var created = false;
        try
        {
            // DDL implicitly commits. The unique table is dropped by its exact name in finally.
            await ExecuteNonQueryAsync(connection,
                $"CREATE TABLE {quotedTable} (\"G\" INT, \"ORD\" INT, \"V\" VARCHAR2(100))");
            created = true;

            foreach (var fixture in QueryTranslationCases.Aggregates)
            {
                for (var index = 0; index < fixture.ValuesInOrder.Length; index++)
                {
                    await InsertAsync(connection, quotedTable, fixture.Id, index, fixture.ValuesInOrder[index]);
                }
            }

            var cases = new List<ProbeCase>();
            foreach (var fixture in QueryTranslationCases.Aggregates)
            {
                var separator = SqlLiteral(fixture.Separator);
                var source = $" FROM {quotedTable} WHERE \"G\" = {fixture.Id}";
                var order = " WITHIN GROUP (ORDER BY \"ORD\")";
                var plain = $"LISTAGG(\"V\", {separator}){order}";
                var coalescedElement = $"LISTAGG(COALESCE(\"V\", ''), {separator}){order}";
                // Every nonempty separator prefix keeps null/empty rows in LISTAGG. Removing
                // exactly the first prefix then reproduces their separator positions without
                // a sentinel that could collide with user data. The empty-separator case
                // reduces to ordinary concatenation and is handled by the outer COALESCE.
                var prefixed = $"COALESCE(SUBSTR(LISTAGG({separator} || COALESCE(\"V\", ''), ''){order}, COALESCE(LENGTH({separator}), 0) + 1), '')";
                var expected = fixture.ExpectedJoin;

                cases.Add(new($"group_{fixture.Id}.plain", $"SELECT {plain}{source}", expected));
                cases.Add(new($"group_{fixture.Id}.element_coalesce", $"SELECT {coalescedElement}{source}", expected));
                cases.Add(new($"group_{fixture.Id}.outer_coalesce", $"SELECT COALESCE({coalescedElement}, ''){source}", expected));
                cases.Add(new($"group_{fixture.Id}.prefixed", $"SELECT {prefixed}{source}", expected));
                cases.Add(new($"group_{fixture.Id}.concat", $"SELECT LISTAGG(\"V\", ''){order}{source}", fixture.ExpectedConcat));
            }

            var unicodeSeparator = SqlLiteral("雪🌙");
            cases.Add(new("group_3.prefixed_unicode_separator",
                $"SELECT COALESCE(SUBSTR(LISTAGG({unicodeSeparator} || COALESCE(\"V\", ''), '') " +
                $"WITHIN GROUP (ORDER BY \"ORD\"), COALESCE(LENGTH({unicodeSeparator}), 0) + 1), '') " +
                $"FROM {quotedTable} WHERE \"G\" = 3",
                "甲雪🌙乙雪🌙甲"));

            // The same group has repeated values. This records whether omitting WITHIN GROUP is
            // accepted, but makes no assertion about the order of an unordered aggregate.
            cases.Add(new("unordered.duplicates", $"SELECT LISTAGG(\"V\", '|') FROM {quotedTable} WHERE \"G\" = 3", null));
            cases.Add(new("unordered.constant_order",
                $"SELECT LISTAGG(\"V\", '|') WITHIN GROUP (ORDER BY 1) FROM {quotedTable} WHERE \"G\" = 3",
                null));
            cases.Add(new("overflow.explicit_error",
                "SELECT LISTAGG(RPAD('x', 200, 'x'), ',') WITHIN GROUP (ORDER BY LEVEL) FROM dual CONNECT BY LEVEL <= 200",
                null));

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var probe in cases)
            {
                var status = await ExecuteCaseAsync(connection, probe);
                counts[status] = counts.GetValueOrDefault(status) + 1;
            }

            output.WriteLine(JsonSerializer.Serialize(new { Status = "summary", Cases = cases.Count, Counts = counts }));
            Assert.Equal(cases.Count, counts.Values.Sum());
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE {quotedTable}");
            }
        }
    }

    private static async Task InsertAsync(DbConnection connection, string quotedTable, int group, int order, string? value)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {quotedTable} (\"G\", \"ORD\", \"V\") VALUES (:group_id, :sort_order, :item_value)";
        command.CommandTimeout = CommandTimeoutSeconds;
        AddParameter(command, "group_id", DbType.Int32, group);
        AddParameter(command, "sort_order", DbType.Int32, order);
        AddParameter(command, "item_value", DbType.String, value ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync();
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
                output.WriteLine(JsonSerializer.Serialize(new { probe.Id, Status = "no_row", probe.Expected, ElapsedMs = watch.ElapsedMilliseconds }));
                return "no_row";
            }

            var raw = reader.GetValue(0);
            var actual = raw is DBNull ? null : Convert.ToString(raw, CultureInfo.InvariantCulture);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                probe.Id,
                Status = "value",
                Value = actual is { Length: > 120 } ? actual[..120] : actual,
                ValueLength = actual?.Length,
                probe.Expected,
                MatchesClr = probe.Expected is null ? (bool?)null : actual == probe.Expected,
                ClrType = raw is DBNull ? null : raw.GetType().FullName,
                FieldType = reader.GetFieldType(0).FullName,
                DataType = reader.GetDataTypeName(0),
                ElapsedMs = watch.ElapsedMilliseconds
            }));
            return "value";
        }
        catch (Exception error) when (error is DbException or InvalidCastException or FormatException or OverflowException or NotSupportedException)
        {
            // Candidate failure is evidence; connection/setup/cleanup failure still fails the test.
            // Avoid printing exception text because drivers can include connection details in it.
            output.WriteLine(JsonSerializer.Serialize(new
            {
                probe.Id,
                Status = error is DbException ? "sql_error" : "read_error",
                ErrorType = error.GetType().Name,
                ErrorCode = (error as DbException)?.ErrorCode,
                ElapsedMs = watch.ElapsedMilliseconds
            }));
            return error is DbException ? "sql_error" : "read_error";
        }
    }

    private static string SqlLiteral(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record ProbeCase(string Id, string Sql, string? Expected);
}
