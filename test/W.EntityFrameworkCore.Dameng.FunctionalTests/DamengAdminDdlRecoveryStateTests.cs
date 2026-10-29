using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Category", "OfflineFixture")]
public sealed class DamengAdminDdlRecoveryStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dameng-recovery-test-" + Guid.NewGuid().ToString("N"));
    private DamengAdminDdlRecoveryState State() => new("OWN_TS", "OWN_U", "/owned/OWN_TS.DBF", _directory);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void JournalIsDurableBeforeCreateIsSent()
    {
        var state = State();
        state.Plan(state.Tablespace, true);
        state.BeforeCreate(state.Tablespace);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(state.JournalPath));
        Assert.Equal((int)AdminDdlPhase.Unknown, json.RootElement.GetProperty("Tablespace").GetProperty("Phase").GetInt32());
        Assert.True(json.RootElement.GetProperty("Tablespace").GetProperty("AbsentBeforeCreate").GetBoolean());
    }

    [Fact]
    public void JournalFailurePreventsCreate()
    {
        var state = State(); state.Plan(state.Tablespace, true);
        state.BeforeSave = () => throw new IOException("synthetic");
        var sends = 0;
        Assert.Throws<IOException>(() => { state.BeforeCreate(state.Tablespace); sends++; });
        Assert.Equal(0, sends);
    }

    [Fact]
    public void JournalContainsNoCredentialSentinel()
    {
        var state = State();
        var json = File.ReadAllText(state.JournalPath);
        Assert.DoesNotContain("Password", json); Assert.DoesNotContain("ConnectionString", json);
        Assert.DoesNotContain("CREATE USER", json); Assert.DoesNotContain("sentinel-secret", json);
        Assert.Contains("OWN_TS", json);
    }

    [Fact]
    public void JournalIsPrivate()
    {
        var state = State();
        if (OperatingSystem.IsWindows()) { Assert.StartsWith(Path.GetTempPath(), state.JournalPath); return; }
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(state.JournalPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_directory));
    }

    [Fact]
    public void UnknownCreateDoesNotRetryCreate()
    {
        var state = State(); state.Plan(state.Tablespace, true); state.BeforeCreate(state.Tablespace);
        Assert.Throws<InvalidOperationException>(() => state.BeforeCreate(state.Tablespace));
    }

    [Fact]
    public void UnknownCreateRequiresFreshConnection()
    {
        var state = State(); state.Plan(state.User, true); state.BeforeCreate(state.User);
        Assert.False(state.CanDrop(state.User, true, false)); Assert.True(state.CanDrop(state.User, true, true));
    }

    [Fact]
    public void MismatchedOwnershipBlocksDrop()
    {
        var state = State(); state.Plan(state.User, true); state.BeforeCreate(state.User);
        Assert.False(state.CanDrop(state.User, false, true));
    }

    [Fact]
    public void CleanupOrdersUserBeforeTablespace()
    {
        var state = State(); state.Plan(state.Tablespace, true); state.BeforeCreate(state.Tablespace);
        state.Plan(state.User, true); state.BeforeCreate(state.User);
        Assert.False(state.CanDrop(state.Tablespace, true, true));
        state.Transition(state.User, AdminDdlPhase.Absent);
        Assert.True(state.CanDrop(state.Tablespace, true, true));
    }

    [Fact]
    public void RepeatedCleanupDoesNotDeleteUnownedObjects()
    {
        var state = State(); Assert.False(state.CanDrop(state.User, true, true));
        state.Plan(state.User, true); state.BeforeCreate(state.User); state.Transition(state.User, AdminDdlPhase.Absent);
        Assert.False(state.CanDrop(state.User, true, true));
    }

    [Fact]
    public void UnresolvedCleanupRetainsRecord()
    {
        var state = State(); state.Finish(false);
        Assert.Contains("cleanup_required", File.ReadAllText(state.JournalPath));
        state.Finish(true); Assert.Contains("cleaned", File.ReadAllText(state.JournalPath));
    }

    [Fact]
    public void ExistingObjectCannotBecomeOwned()
    {
        var state = State(); Assert.Throws<InvalidOperationException>(() => state.Plan(state.User, false));
        Assert.False(state.CanDrop(state.User, true, true));
    }

    [Fact]
    public void SendingWithoutPlanIsRejected()
    {
        var state = State(); Assert.Throws<InvalidOperationException>(() => state.BeforeCreate(state.User));
    }
    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)][InlineData(true, true)]
    public async Task IndependentCleanupAlwaysAttemptsBothAndRetainsFailures(bool firstFails, bool secondFails)
    {
        var calls = new List<string>();
        var first = new InvalidOperationException("synthetic-first-cleanup");
        var second = new IOException("synthetic-second-cleanup");
        var failure = await Record.ExceptionAsync(() => DamengAdminDdlRecoveryState.CleanupIndependentAsync(
            () => { calls.Add("first"); return firstFails ? Task.FromException(first) : Task.CompletedTask; },
            () => { calls.Add("second"); return secondFails ? Task.FromException(second) : Task.CompletedTask; }));
        Assert.Equal(["first", "second"], calls);
        if (firstFails && secondFails)
            Assert.Equal(new Exception[] { first, second }, Assert.IsType<AggregateException>(failure).InnerExceptions);
        else if (firstFails) Assert.Same(first, failure);
        else if (secondFails) Assert.Same(second, failure);
        else Assert.Null(failure);
    }

}
