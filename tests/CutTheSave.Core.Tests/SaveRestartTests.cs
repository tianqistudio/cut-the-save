using System.Diagnostics;
using System.Reflection;
using CutTheSave.Core;

internal static class SaveRestartTests
{
    internal static int RunChild(string[] args)
    {
        if (args.Length != 5) return 81;
        var root = Path.GetFullPath(args[1]);
        if (!Guid.TryParseExact(args[2], "N", out _) || !Enum.TryParse<SaveWriteStage>(args[3], out var stop)
            || !Path.GetFileName(root).StartsWith("CutTheSave-save-restart-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || File.ReadAllText(Path.Combine(root, "fixture-token")) != args[2]) return 82;
        new AtomicSaveService(stage =>
        {
            if (stage == stop) Environment.Exit(80);
            if (args[4] == "rollback" && stage == SaveWriteStage.AfterReplace) throw new IOException("fixture rollback");
        }).Save(Path.Combine(root, "save.bin"), Save(20), Path.Combine(root, "backups"));
        return 83;
    }

    internal static void Run()
    {
        foreach (var exists in new[] { false, true })
        foreach (var stage in new[] { SaveWriteStage.IntentFlushed, SaveWriteStage.TempValidated, SaveWriteStage.BeforeReplace,
            SaveWriteStage.AfterReplace, SaveWriteStage.BeforeCleanup })
            Fixture((root, store, target) =>
            {
                if (!exists) File.Delete(target);
                Crash(root, stage);
                var status = store.InspectInterruptedSave();
                var outputPresent = stage is SaveWriteStage.AfterReplace or SaveWriteStage.BeforeCleanup;
                Check(status.State == (outputPresent ? InterruptedSaveState.OutputPresent : InterruptedSaveState.OriginalPresent), "restart classifies " + stage);
                Check(status.CanKeepCurrent && status.CanRestorePrevious == (exists && outputPresent), "restart offers only verified choices");
                var snapshot = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                store.InspectInterruptedSave();
                foreach (var file in snapshot) Check(File.ReadAllBytes(file.Key).SequenceEqual(file.Value), "startup inspection is read-only");
                Throws<SaveRecoveryPendingException>(() => new AtomicSaveService().Save(Path.Combine(root, "new.bin"), Save(30), store.BackupDirectory));
                Throws<SaveRecoveryPendingException>(() => store.PlanRetention());
                if (exists)
                {
                    var prewrite = store.List().Single();
                    Throws<SaveRecoveryPendingException>(() => store.SetLocked(prewrite.Path, true));
                    Throws<SaveRecoveryPendingException>(() => store.Restore(prewrite.Path, target));
                }
                var resolution = store.ResolveInterruptedSave(status, restorePrevious: exists && outputPresent);
                Check(store.InspectInterruptedSave().State == InterruptedSaveState.None && File.Exists(resolution.ArchivePath), "resolved journal archived without replay");
                Check(File.Exists(target) == (exists || outputPresent), "resolution preserves absent new target");
                if (exists)
                {
                    Check(Score(target) == 10 && store.List().All(record => record.IsLocked), "previous save restored/preserved and recovery snapshots locked");
                    Check(store.List().Count == (outputPresent ? 2 : 1), "explicit restart restore also indexes the displaced current output");
                    if (outputPresent) Check(store.List().Any(record => record.Reason == "recovery-pre-restore" && Score(record.Path) == 20),
                        "pre-restore output remains recoverable after transaction cleanup");
                }
                else if (outputPresent) Check(Score(target) == 20, "new output kept, never silently removed");
            });
        Fixture((root, store, target) =>
        {
            Crash(root, SaveWriteStage.AfterRollback, "rollback");
            Check(store.InspectInterruptedSave().State == InterruptedSaveState.OriginalPresent && Score(target) == 10, "process exit during rollback recognizes actual prior bytes");
            store.ResolveInterruptedSave(store.InspectInterruptedSave(), false);
        });
        Fixture(ChangedTarget);
        Fixture(PartialTemp);
        Fixture(CorruptJournal);
        Fixture(ChangedPriorBackup);
        Fixture(IndexWriteFailure);
        foreach (var stop in Enum.GetValues<SaveRestartStage>())
            Fixture((root, store, target) =>
            {
                Crash(root, SaveWriteStage.AfterReplace);
                Throws<OperationCanceledException>(() => store.ResolveInterruptedSave(store.InspectInterruptedSave(), true,
                    stage => { if (stage == stop) throw new OperationCanceledException(); }));
                var reopened = new BackupService(store.BackupDirectory);
                Check(Score(target) == 10 && reopened.InspectInterruptedSave().State == InterruptedSaveState.OriginalPresent, "interrupted recovery is recognizable");
                reopened.ResolveInterruptedSave(reopened.InspectInterruptedSave(), false);
                Check(Score(target) == 10, "retry does not replay original write");
            });
        Console.WriteLine("PASS save restart: 11 actual process exits, read-only discovery, safe choices, stale/changed/partial/corrupt states, locked prior backups and recovery retries");
    }

    private static void ChangedTarget(string root, BackupService store, string target)
    {
        Crash(root, SaveWriteStage.AfterReplace);
        var review = store.InspectInterruptedSave();
        Write(target, 99);
        Throws<SaveTargetChangedException>(() => store.ResolveInterruptedSave(review, true));
        var changed = store.InspectInterruptedSave();
        Check(changed.State == InterruptedSaveState.Changed && !changed.CanRestorePrevious, "external updates disable overwrite recovery");
        store.ResolveInterruptedSave(changed, false);
        Check(Score(target) == 99, "explicit keep current does not overwrite external updates");
    }

    private static void PartialTemp(string root, BackupService store, string target)
    {
        Crash(root, SaveWriteStage.TempValidated);
        var temp = Directory.EnumerateFiles(root, "*.cts.tmp").Single();
        File.WriteAllText(temp, "partial/unrecognized bytes");
        var status = store.InspectInterruptedSave();
        Check(status.CanKeepCurrent && status.RetainedPaths!.Contains(temp), "partial staged file is disclosed");
        var resolved = store.ResolveInterruptedSave(status, false);
        Check(resolved.RetainedPaths.Contains(temp) && File.ReadAllText(temp) == "partial/unrecognized bytes" && Score(target) == 10,
            "partial/unknown leftovers retained while explicit keep-current leaves target untouched");
    }

    private static void CorruptJournal(string root, BackupService store, string target)
    {
        Crash(root, SaveWriteStage.IntentFlushed);
        var journal = Path.Combine(store.BackupDirectory, "save-transaction.json");
        File.WriteAllText(journal, "{ corrupt");
        var status = store.InspectInterruptedSave();
        Check(status.State == InterruptedSaveState.NeedsReview && !status.CanKeepCurrent && !status.CanRestorePrevious, "corrupt journal cannot authorize path writes");
        Throws<SaveTargetChangedException>(() => store.ResolveInterruptedSave(status, false));
        Check(File.ReadAllText(journal) == "{ corrupt" && Score(target) == 10, "corrupt journal is not reset");
    }

    private static void ChangedPriorBackup(string root, BackupService store, string target)
    {
        Crash(root, SaveWriteStage.AfterReplace);
        File.WriteAllText(store.List().Single().Path, "changed prior snapshot");
        Check(store.InspectInterruptedSave().State == InterruptedSaveState.NeedsReview, "changed prior backup blocks both resolution choices");
        Check(Score(target) == 20, "changed prior backup never replaces the output");
    }

    private static void IndexWriteFailure(string root, BackupService store, string target)
    {
        if (!OperatingSystem.IsWindows()) return;
        Crash(root, SaveWriteStage.AfterReplace);
        using (var guard = File.OpenRead(Path.Combine(store.BackupDirectory, "backup-index.json")))
            Throws<IOException>(() => store.ResolveInterruptedSave(store.InspectInterruptedSave(), true));
        Check(Score(target) == 20, "cannot restore before old-backup protection persists");
        store.ResolveInterruptedSave(store.InspectInterruptedSave(), true);
        Check(Score(target) == 10, "retry after sharing release succeeds");
    }

    private static void Crash(string root, SaveWriteStage stage, string mode = "save")
    {
        var token = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(root, "fixture-token"), token);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var value in new[] { "--save-crash", root, token, stage.ToString(), mode }) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(20_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new IOException("Owned save-crash child timed out."); }
        Check(process.ExitCode == 80, "owned child exited at " + stage);
    }
    private static CutTheRopeSave Save(int score) { var save = new CutTheRopeSave(); save.SetInt("SCORE_0_0", score); return save; }
    private static void Write(string path, int score) { using var output = File.Create(path); new SaveCodec().Write(output, Save(score)); }
    private static int Score(string path) { using var input = File.OpenRead(path); return new SaveCodec().Read(input).IntEntries.Single().Value; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Fixture(Action<string, BackupService, string> test)
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-save-restart-");
        try { var target = Path.Combine(root.FullName, "save.bin"); Write(target, 10); test(root.FullName, new BackupService(Path.Combine(root.FullName, "backups")), target); }
        finally { if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true); }
    }
}
