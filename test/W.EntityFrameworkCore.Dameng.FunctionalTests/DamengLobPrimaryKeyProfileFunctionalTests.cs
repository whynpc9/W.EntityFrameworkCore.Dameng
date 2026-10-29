using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Profile", "PkWithCluster1")]
[Trait("Category", "LobPrimaryKey")]
public sealed class DamengLobPrimaryKeyProfileFunctionalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ClusteredDefaultProfileIsVerifiedBeforeDdl()
    {
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        await DamengLobPrimaryKeyHarness.VerifyProfile(connection, output);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExplicitNonclusteredServerControlRoundtrips(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "raw", false, true, output);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task UnannotatedServerDdlMatchesRecordedProfileOutcome(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "raw", null, true, output);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExistingGeneratedLobPathIsProtectedWhenDefaultPrimaryKeyIsClustered(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "script", null, true, output);

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExistingExplicitClusteredPathMatchesRecordedProfileContract(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "manual", true, true, output);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExistingMigrationPathMatchesRecordedClusteredDefaultContract(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "diff", null, true, output);

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task SharedTableLobPathRetainsVerifiedServerProtection(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "shared", null, true, output);

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task AddPrimaryKeyRetainsVerifiedServerProtection(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "addpk", null, true, output);

    [Fact]
    public Task NonLobControlActuallyUsesClusteredDefault()
        => DamengLobPrimaryKeyHarness.Roundtrip(5, "raw", null, true, output);
}
