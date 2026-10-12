using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using CutTheSave.Core;

internal static class BackupRecoveryTests
{
    internal static int RunChild(string[] args)
    {
        if (args.Length != 4 || args[0] != "--retention-crash") return -1;
        var root = Path.GetFullPath(args[1]);
        if (!Guid.TryParseExact(args[2], "N", out _) || !Enum.TryParse<BackupRetentionStage>(args[3], out var stop)
            || !Path.GetFileName(root).StartsWith("CutTheSave-retention-restart-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || File.ReadAllText(Path.Combine(root, "fixture-token")) != args[2]) return 73;
        var store = new BackupService(Path.Combine(root, "backups"), stage =>
        {
            if (stage == stop) Environment.Exit(72); // Abrupt process exit: no finally/recovery runs.
        });
        store.ApplyRetention(store.PlanRetention(new BackupRetentionPolicy(1, long.MaxValue)));
        return 74;
    }

    internal static void Run(bool useChildProcesses = true)
    {
        foreach (var stop in Enum.GetValues<BackupRetentionStage>())
            Fixture((root, store, records) =>
            {
                var saveBytes = File.ReadAllBytes(Path.Combine(root, "save.bin"));
                var before = File.ReadAllBytes(records[0].Path);
                if (useChildProcesses) CrashChild(root, stop);
                else Interrupt(store.BackupDirectory, stop);
                var restarted = new BackupService(store.BackupDirectory);
                var committed = stop >= BackupRetentionStage.IndexCommitted;
                var expected = committed ? BackupMaintenanceState.FinalizePending : BackupMaintenanceState.RollbackPending;
                var journalBefore = File.ReadAllBytes(Journal(store));
                Check(restarted.InspectMaintenance().State == expected, "restart identifies exact index boundary at " + stop);
                Check(journalBefore.SequenceEqual(File.ReadAllBytes(Journal(store))), "inspection is read-only");
                Throws<BackupMaintenancePendingException>(() => restarted.CreateSnapshot(Path.Combine(root, "save.bin"), "blocked"));
                Throws<BackupMaintenancePendingException>(() => restarted.SetLocked(records[^1].Path, true));
                Throws<BackupMaintenancePendingException>(() => restarted.PlanRetention());
                Throws<BackupMaintenancePendingException>(() => restarted.Restore(records[^1].Path, Path.Combine(root, "save.bin")));
                var output = Path.Combine(root, "new.bin");
                Throws<BackupMaintenancePendingException>(() => new AtomicSaveService().Save(output, Save(99), store.BackupDirectory));
                Check(!File.Exists(output), "pending cleanup blocks even a new save target");
                Check(restarted.RecoverInterruptedRetention().State == BackupMaintenanceState.None, "explicit recovery completes");
                Check(restarted.RecoverInterruptedRetention().State == BackupMaintenanceState.None, "recovery is idempotent");
                Check(restarted.List().Count == (committed ? 1 : 2), "recovery respects committed index");
                if (!committed) Check(File.ReadAllBytes(records[0].Path).SequenceEqual(before), "uncommitted backup restored byte-exact");
                else Check(!File.Exists(records[0].Path), "committed expiry remains removed");
                Check(saveBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "save.bin"))), "recovery never changes game save");
                Check(!File.Exists(Journal(store)) && !Directory.EnumerateFiles(store.BackupDirectory, "*.quarantine").Any(), "no transaction files left after recovery");
            });

        Fixture(IndexCommitFailure);
        foreach (var change in new[] { "index", "journal", "quarantine", "missing", "conflict", "outside" })
            Fixture((root, store, records) => Conflicts(root, store, records, change));
        Fixture(BusyRecovery);
        foreach (var stop in new[] { BackupRetentionStage.SnapshotQuarantined, BackupRetentionStage.IndexCommitted })
            Fixture((root, store, records) => InterruptedRecovery(root, store, records, stop));
        Console.WriteLine("PASS retention recovery: four exit boundaries, index-write failure, six conflicts, blocked mutations and idempotent recovery (isolated fixtures)");
    }

    private static void IndexCommitFailure(string root, BackupService store, BackupRecord[] records)
    {
        FileStream? indexLock = null;
        try
        {
            var fault = new BackupService(store.BackupDirectory, stage =>
            {
                if (stage == BackupRetentionStage.SnapshotQuarantined)
                    indexLock = new FileStream(Path.Combine(store.BackupDirectory, "backup-index.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            if (!OperatingSystem.IsWindows()) return;
            Throws<BackupMaintenancePendingException>(() => fault.ApplyRetention(fault.PlanRetention(new BackupRetentionPolicy(1, long.MaxValue))));
        }
        finally { indexLock?.Dispose(); }
        Check(store.InspectMaintenance().State == BackupMaintenanceState.RollbackPending, "failed index persistence retains recoverable quarantine");
        Check(!File.Exists(records[0].Path), "snapshot is quarantined, not deleted");
        Check(store.RecoverInterruptedRetention().State == BackupMaintenanceState.None && File.Exists(records[0].Path), "index failure restores the exact backup");
    }

    private static void Conflicts(string root, BackupService store, BackupRecord[] records, string change)
    {
        Interrupt(store.BackupDirectory, BackupRetentionStage.SnapshotQuarantined);
        var quarantine = Directory.EnumerateFiles(store.BackupDirectory, "*.quarantine").Single();
        var journal = Journal(store);
        switch (change)
        {
            case "index": File.WriteAllText(Path.Combine(store.BackupDirectory, "backup-index.json"), "[]"); break;
            case "journal": File.WriteAllText(journal, "{ truncated"); break;
            case "quarantine": File.WriteAllText(quarantine, "changed"); break;
            case "missing": File.Delete(quarantine); break;
            case "conflict": File.WriteAllText(records[0].Path, "external replacement"); break;
            case "outside":
                var node = JsonNode.Parse(File.ReadAllText(journal))!;
                node["Candidate"]!["Record"]!["Path"] = Path.Combine(root, "external.bin");
                File.WriteAllText(journal, node.ToJsonString());
                break;
        }
        var before = Directory.EnumerateFiles(store.BackupDirectory).ToDictionary(path => path, File.ReadAllBytes);
        Check(store.InspectMaintenance().State == BackupMaintenanceState.NeedsReview, change + " prevents automatic recovery");
        Check(store.RecoverInterruptedRetention().State == BackupMaintenanceState.NeedsReview, change + " is not silently acknowledged");
        foreach (var file in before) Check(File.ReadAllBytes(file.Key).SequenceEqual(file.Value), change + " retains every remaining file");
    }

    private static void BusyRecovery(string root, BackupService store, BackupRecord[] records)
    {
        Interrupt(store.BackupDirectory, BackupRetentionStage.IndexCommitted);
        var quarantine = Directory.EnumerateFiles(store.BackupDirectory, "*.quarantine").Single();
        using (var guard = new FileStream(quarantine, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (OperatingSystem.IsWindows()) Throws<IOException>(() => store.RecoverInterruptedRetention());
        }
        Check(store.RecoverInterruptedRetention().State == BackupMaintenanceState.None, "busy recovery retries after handle release");
    }

    private static void InterruptedRecovery(string root, BackupService store, BackupRecord[] records, BackupRetentionStage stop)
    {
        if (!OperatingSystem.IsWindows()) return;
        Interrupt(store.BackupDirectory, stop);
        using (var guard = new FileStream(Journal(store), FileMode.Open, FileAccess.Read, FileShare.Read))
            Throws<IOException>(() => store.RecoverInterruptedRetention());
        Check(!Directory.EnumerateFiles(store.BackupDirectory, "*.quarantine").Any(), "recovery action completed before journal cleanup was blocked");
        Check(File.Exists(records[0].Path) == (stop == BackupRetentionStage.SnapshotQuarantined), "interrupted recovery has the correct backup state");
        Check(new BackupService(store.BackupDirectory).RecoverInterruptedRetention().State == BackupMaintenanceState.None,
            "a fresh service completes interrupted recovery without repeating removal or overwriting");
    }

    private static void Interrupt(string directory, BackupRetentionStage stop)
    {
        var store = new BackupService(directory, stage => { if (stage == stop) throw new OperationCanceledException("test interruption"); });
        Throws<OperationCanceledException>(() => store.ApplyRetention(store.PlanRetention(new BackupRetentionPolicy(1, long.MaxValue))));
    }

    private static void CrashChild(string root, BackupRetentionStage stop)
    {
        var token = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(root, "fixture-token"), token);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var value in new[] { "--retention-crash", root, token, stop.ToString() }) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true); // Only this test-owned child.
            process.WaitForExit();
            throw new InvalidOperationException("Owned crash-test child timed out.");
        }
        Check(process.ExitCode == 72, "child exited at requested boundary: " + stop);
    }

    private static string Journal(BackupService store) => Path.Combine(store.BackupDirectory, "retention-transaction.json");
    private static CutTheRopeSave Save(int score) { var save = new CutTheRopeSave(); save.SetInt("SCORE_0_0", score); return save; }
    private static void Fixture(Action<string, BackupService, BackupRecord[]> test)
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-retention-restart-");
        try
        {
            var store = new BackupService(Path.Combine(root.FullName, "backups"));
            var target = Path.Combine(root.FullName, "save.bin");
            var records = Enumerable.Range(0, 2).Select(index =>
            {
                using (var stream = File.Create(target)) new SaveCodec().Write(stream, Save(index));
                return store.CreateSnapshot(target, "fixture")!;
            }).ToArray();
            test(root.FullName, store, records);
        }
        finally { if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
