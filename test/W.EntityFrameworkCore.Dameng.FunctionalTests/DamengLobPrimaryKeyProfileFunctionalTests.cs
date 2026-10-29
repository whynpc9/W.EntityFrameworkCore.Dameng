using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

// Only an environment preflight until independent server characterization has
// fixed the LOB outcome matrix. Passing this alone cannot close plan 019.
[Trait("Profile", "PkWithCluster1")]
[Trait("Category", "LobPrimaryKey")]
public sealed class DamengLobPrimaryKeyProfileFunctionalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ClusteredDefaultProfileIsVerifiedBeforeDdl()
    {
        Assert.Equal("pk-cluster-1", Environment.GetEnvironmentVariable("DAMENG_TEST_PROFILE"));
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")));
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME='PK_WITH_CLUSTER'";
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        Assert.Equal(1, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        command.CommandText = "SELECT PAGE() FROM dual";
        output.WriteLine("Server=" + connection.ServerVersion + "; PK_WITH_CLUSTER=1; page="
            + Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        output.WriteLine("Read-only preflight only; no LOB server outcome or cleanup evidence is claimed.");
    }
}
