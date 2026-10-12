using System.Security.Cryptography;
using System.Text.Json;
using CutTheSave.Core;

internal static class BackupSafetyTests
{
    internal static void Run()
    {
        InFixture(IndexedPrewrite);
        InFixture(RetentionAndLocks);
        InFixture(DefaultRetention);
        InFixture(ByteLimit);
        InFixture(LegacyAndUnknown);
        InFixture(IndexFailure);
        InFixture(TamperAndBusyFiles);
        InFixture(ConcurrentSnapshots);
        Check(new BackupRetentionPolicy() == new BackupRetentionPolicy(50, 200L * 1024 * 1024), "default retention limits");
    }

    private static void IndexedPrewrite(string root)
    {
        var target = Path.Combine(root, "save.bin");
        var store = new BackupService(Path.Combine(root, "backups"));
        WriteSave(target, 10);
        var original = File.ReadAllBytes(target);
        var oldTemp = target + ".cts.tmp";
        File.WriteAllText(oldTemp, "unrelated temporary file");
        new AtomicSaveService().Save(target, Save(20), store.BackupDirectory);
        var before = store.List(target).Single();
        Check(before.Reason == "pre-write" && before.RetentionEligible && before.FileSize == original.Length, "prewrite metadata is indexed");
        Check(File.ReadAllBytes(before.Path).SequenceEqual(original), "prewrite bytes match the overwritten original");
        Check(before.Sha256 == Hash(original), "prewrite hash matches its content");
        Check(File.ReadAllText(oldTemp) == "unrelated temporary file", "old deterministic temp name is untouched");
        store.Restore(before.Path, target);
        Check(File.ReadAllBytes(target).SequenceEqual(original), "restore is byte-exact");
        Check(store.List(target).Any(record => record.Reason == "recovery-pre-restore"), "restore creates indexed recovery snapshot");
        var a = store.CreateSnapshot(target, "open", true)!;
        var b = store.CreateSnapshot(target, "open", true)!;
        Check(a.Path == b.Path, "same-day identical open snapshot is reused");
        Check(store.Verify(a.Path).Sha256 == a.Sha256, "verified metadata can be used for hash copying");
    }

    private static void RetentionAndLocks(string root)
    {
        var target = Path.Combine(root, "save.bin");
        var store = new BackupService(Path.Combine(root, "backups"));
        var snapshots = Snapshots(store, target, 6);
        store.SetLocked(snapshots[0].Path, true);
        var plan = store.PlanRetention(new BackupRetentionPolicy(3, long.MaxValue), [snapshots[1].Path]);
        Check(plan.Candidates.Count == 3, "count retention selects only excess snapshots");
        Check(!plan.Candidates.Any(item => item.Record.Path == snapshots[0].Path || item.Record.Path == snapshots[1].Path || item.Record.Path == snapshots[5].Path), "lock, session and newest snapshots protected");
        var newlyLocked = plan.Candidates[0].Record;
        store.SetLocked(newlyLocked.Path, true);
        var result = store.ApplyRetention(plan);
        Check(result.DeletedCount == 2 && File.Exists(newlyLocked.Path), "locking after preview prevents stale-plan deletion");
        Check(store.List().Count == 4 && store.List().Count(record => record.IsLocked) == 2, "index retains both locks");
        Check(store.PlanRetention(new BackupRetentionPolicy(1, 1), [snapshots[1].Path]).LimitsRemainExceeded, "safety exemptions report an exceeded limit");
        store.SetLocked(newlyLocked.Path, false);
        Check(!store.List().Single(record => record.Path == newlyLocked.Path).IsLocked, "unlock persists");
    }

    private static void DefaultRetention(string root)
    {
        var store = new BackupService(Path.Combine(root, "backups"));
        var snapshots = Snapshots(store, Path.Combine(root, "save.bin"), 52);
        var result = store.EnforceRetention();
        Check(result.DeletedCount == 2 && result.Issues.Count == 0, "default policy removes exactly two of 52 eligible snapshots");
        Check(store.List().Count == 50 && File.Exists(snapshots[^1].Path), "default policy keeps 50 snapshots including the newest");
    }

    private static void ByteLimit(string root)
    {
        var store = new BackupService(Path.Combine(root, "backups"));
        var records = Snapshots(store, Path.Combine(root, "save.bin"), 3);
        var bytes = records[0].FileSize;
        var plan = store.PlanRetention(new BackupRetentionPolicy(99, bytes * 2));
        Check(plan.Candidates.Count == 1, "byte cap applies before count cap");
        Check(store.ApplyRetention(plan).DeletedBytes == bytes, "removed byte count is exact");
        var tiny = store.PlanRetention(new BackupRetentionPolicy(99, 1));
        store.ApplyRetention(tiny);
        Check(store.List().Count == 1 && File.Exists(records[^1].Path), "newest recoverable snapshot survives even an undersized quota");
    }

    private static void LegacyAndUnknown(string root)
    {
        var store = new BackupService(Path.Combine(root, "backups"));
        var records = Snapshots(store, Path.Combine(root, "save.bin"), 3);
        var legacy = records[0] with { RetentionEligible = false };
        var outside = Path.Combine(root, "cts-v1-" + Guid.NewGuid().ToString("N") + "_outside.bin");
        File.Copy(records[1].Path, outside);
        var escaped = records[1] with { Path = outside };
        File.WriteAllText(Path.Combine(store.BackupDirectory, "backup-index.json"), JsonSerializer.Serialize(new[] { legacy, records[1], records[2], escaped }));
        var unknown = Path.Combine(store.BackupDirectory, "unknown.bin");
        File.WriteAllText(unknown, "not owned by the backup index");
        var plan = store.PlanRetention(new BackupRetentionPolicy(1, 1));
        store.ApplyRetention(plan);
        Check(File.Exists(legacy.Path) && File.Exists(outside) && File.Exists(unknown), "legacy, outside and unindexed files never become inferred cleanup targets");
        Check(store.List().Any(record => record.Path == escaped.Path), "outside metadata is preserved for review");
    }

    private static void IndexFailure(string root)
    {
        var target = Path.Combine(root, "save.bin");
        WriteSave(target, 10);
        var store = new BackupService(Path.Combine(root, "backups"));
        store.CreateSnapshot(target, "open");
        var index = Path.Combine(store.BackupDirectory, "backup-index.json");
        File.WriteAllText(index, "{ damaged");
        var original = File.ReadAllBytes(target);
        Throws<IOException>(() => new AtomicSaveService().Save(target, Save(20), store.BackupDirectory));
        Check(File.ReadAllBytes(target).SequenceEqual(original), "failed backup index prevents replacement");
        Check(File.ReadAllText(index) == "{ damaged", "damaged history is not replaced with an empty index");
        Check(!Directory.GetFiles(root, "*.cts.tmp").Any(), "failed write removes only its own new temporary file");
        File.WriteAllText(index, JsonSerializer.Serialize(new[]
        {
            new BackupRecord("invalid\0path", target, DateTimeOffset.UtcNow, "open", new string('a', 64), 1)
        }));
        Throws<IOException>(() => store.PlanRetention());
        Check(File.ReadAllBytes(target).SequenceEqual(original), "malformed index paths fail safely without touching the save");
    }

    private static void TamperAndBusyFiles(string root)
    {
        var target = Path.Combine(root, "save.bin");
        var store = new BackupService(Path.Combine(root, "backups"));
        var records = Snapshots(store, target, 3);
        var original = File.ReadAllBytes(target);
        WriteSave(records[0].Path, 44);
        Throws<IOException>(() => store.Restore(records[0].Path, target));
        Check(File.ReadAllBytes(target).SequenceEqual(original), "tampered backup cannot overwrite the target");
        var plan = store.PlanRetention(new BackupRetentionPolicy(1, 1));
        Check(!plan.Candidates.Any(item => item.Record.Path == records[0].Path) && plan.Issues.Count > 0, "tampered snapshot is not automatically deleted");
        using (store.ProtectSnapshot(records[1].Path))
        {
            var result = store.ApplyRetention(plan);
            if (OperatingSystem.IsWindows())
                Check(result.DeletedCount == 0 && File.Exists(records[1].Path), "busy snapshot stays indexed for a later retry");
        }
        if (OperatingSystem.IsWindows())
            Check(store.ApplyRetention(store.PlanRetention(new BackupRetentionPolicy(1, 1))).DeletedCount == 1, "a later exact plan can retry after file release");
    }

    private static void ConcurrentSnapshots(string root)
    {
        var target = Path.Combine(root, "save.bin");
        WriteSave(target, 10);
        var directory = Path.Combine(root, "backups");
        var successes = 0;
        Parallel.For(0, 12, _ =>
        {
            try { new BackupService(directory).CreateSnapshot(target, "parallel"); Interlocked.Increment(ref successes); }
            catch (IOException exception) when (exception.Message.Contains("busy", StringComparison.Ordinal)) { }
        });
        Check(successes > 0 && new BackupService(directory).List().Count == successes, "concurrent operations never silently overwrite index entries");
    }

    private static BackupRecord[] Snapshots(BackupService service, string target, int count) =>
        Enumerable.Range(0, count).Select(index => { WriteSave(target, index); return service.CreateSnapshot(target, "test")!; }).ToArray();
    private static CutTheRopeSave Save(int score)
    {
        var save = new CutTheRopeSave();
        save.SetInt("SCORE_0_0", score);
        save.SetString("PREFS_SCORE_HASH", new ScoreHashService().Compute(score));
        return save;
    }
    private static void WriteSave(string path, int score)
    {
        using var stream = File.Create(path);
        new SaveCodec().Write(stream, Save(score));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void InFixture(Action<string> action)
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-backup-safety-");
        try { action(root.FullName); }
        finally { if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true); }
    }
}
