using System.Globalization;
using Dm;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengDateTimeStringCapabilityProbeTests(ITestOutputHelper output)
{
    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task DateAndTimestampTextConversions()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        string[] sources =
        [
            "CAST('2024-01-01' AS DATE)",
            "CAST('2024-02-29 12:30:45.1234567' AS TIMESTAMP(7))",
            "TO_TIMESTAMP('2024-02-29 12:30:45.1234567', 'YYYY-MM-DD HH24:MI:SS.FF7')",
            "TO_TIMESTAMP('2024-02-29 12:30:45.1234568', 'YYYY-MM-DD HH24:MI:SS.FF7')",
            "CAST('0001-01-01 00:00:00' AS TIMESTAMP(7))",
            "CAST('9999-12-31 23:59:59.9999999' AS TIMESTAMP(7))",
            "CAST(NULL AS TIMESTAMP(7))"
        ];
        string[] conversions =
        [
            "CAST({0} AS VARCHAR(100))",
            "TO_CHAR({0})",
            "TO_CHAR({0}, 'YYYY-MM-DD HH24:MI:SS.FF7')",
            "TO_CHAR({0}, 'YYYY-MM')",
            "TO_CHAR({0}, 'YYYY-MM-DD')",
            "TO_CHAR({0}, 'YYYY-MM-DD\"T\"HH24:MI:SS')"
        ];
        foreach (var source in sources)
        {
            foreach (var conversion in conversions)
            {
                await using var command = connection.CreateCommand();
                var expression = string.Format(CultureInfo.InvariantCulture, conversion, source);
                command.CommandText = "SELECT " + expression + " FROM dual";
                try
                {
                    var value = await command.ExecuteScalarAsync();
                    output.WriteLine(expression + " => " + (value is DBNull ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture)));
                }
                catch (DmException error)
                {
                    output.WriteLine(expression + " => rejected (" + error.ErrorCode.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
        }
    }
}
