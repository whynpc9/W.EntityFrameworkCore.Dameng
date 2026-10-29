using System.Text.Json;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

internal enum AdminDdlPhase { NotAttempted, Planned, Unknown, Present, CleanupRequired, Absent }

// Test-only ownership journal. No credentials or SQL are accepted by this type.
internal sealed class DamengAdminDdlRecoveryState
{
    internal sealed class OwnedObject(string kind, string name, string relationship)
    {
        public string Kind { get; } = kind;
        public string Name { get; } = name;
        public string Relationship { get; } = relationship;
        public bool AbsentBeforeCreate { get; set; }
        public AdminDdlPhase Phase { get; set; }
    }

    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public OwnedObject Tablespace { get; }
    public OwnedObject User { get; }
    public string Status { get; private set; } = "planned";
    public DateTimeOffset Updated { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string JournalPath { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal Action? BeforeSave { get; set; }

    internal DamengAdminDdlRecoveryState(string tablespace, string user, string datafile, string? directory = null)
    {
        Tablespace = new("TABLESPACE", tablespace, datafile);
        User = new("USER", user, tablespace);
        directory ??= Environment.GetEnvironmentVariable("DAMENG_TEST_RECOVERY_DIRECTORY")
            ?? Path.Combine(Path.GetTempPath(), "dameng-admin-recovery-" + RunId);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        JournalPath = Path.Combine(directory, RunId + ".json");
        Save();
    }

    internal void Plan(OwnedObject item, bool absent)
    {
        if (item.Phase != AdminDdlPhase.NotAttempted || !absent)
            throw new InvalidOperationException("Exact object must be absent before CREATE.");
        item.AbsentBeforeCreate = true;
        Transition(item, AdminDdlPhase.Planned);
    }

    internal void BeforeCreate(OwnedObject item)
    {
        if (!item.AbsentBeforeCreate || item.Phase != AdminDdlPhase.Planned)
            throw new InvalidOperationException("CREATE cannot be retried or sent without ownership evidence.");
        Transition(item, AdminDdlPhase.Unknown); // Durable before caller sends CREATE.
    }

    internal bool CanDrop(OwnedObject item, bool relationshipMatches, bool freshConnection)
        => item.AbsentBeforeCreate && relationshipMatches && freshConnection
            && item.Phase is AdminDdlPhase.Unknown or AdminDdlPhase.Present or AdminDdlPhase.CleanupRequired
            && (item != Tablespace || User.Phase is AdminDdlPhase.Absent or AdminDdlPhase.NotAttempted or AdminDdlPhase.Planned);

    internal void Transition(OwnedObject item, AdminDdlPhase phase)
    {
        item.Phase = phase;
        Save();
    }

    internal void Finish(bool complete)
    {
        Status = complete ? "cleaned" : "cleanup_required";
        Save();
    }

    internal static async Task CleanupIndependentAsync(params Func<Task>[] cleanups)
    {
        List<Exception> failures = [];
        foreach (var cleanup in cleanups)
        {
            try { await cleanup(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private void Save()
    {
        BeforeSave?.Invoke();
        Updated = DateTimeOffset.UtcNow;
        var temporary = JournalPath + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, this);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, JournalPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
