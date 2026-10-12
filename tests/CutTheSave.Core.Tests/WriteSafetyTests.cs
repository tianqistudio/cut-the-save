using System.Text;
using CutTheSave.Core;

internal static class WriteSafetyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Codec boundaries", CodecBoundaries);
        yield return ("Bounded destination and recovery reads", () => Fixture(BoundedFileIo));
        yield return ("WarningReadOnly", () => Fixture(WarningReadOnly));
        yield return ("StaleDestination", () => Fixture(StaleDestination));
        yield return ("PrecommitChanges", () => Fixture(PrecommitChanges));
        yield return ("PostcommitChanges", () => Fixture(PostcommitChanges));
        yield return ("RollbackFailures", () => Fixture(RollbackFailures));
        yield return ("CommitRenameRace", () => Fixture(CommitRenameRace));
        yield return ("NativeCommitFailure", () => Fixture(NativeCommitFailure));
        yield return ("BusyWriter", () => Fixture(BusyWriter));
        yield return ("CleanupFailure", () => Fixture(CleanupFailure));
        yield return ("TargetCoordination", () => Fixture(TargetCoordination));
    }

    private static void CodecBoundaries()
    {
        var codec = new SaveCodec();
        byte[][] malformed =
        [
            [1, 0, 0, 0, 1, 0xff, 0, 0, 0, 0, 0, 0, 0, 0],
            [1, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [1, 0, 0, 0, 0xff, 0xff, 0xff, 0xff, 0x7f],
            [1, 0, 0, 0, 10, 65],
            [0, 0, 0]
        ];
        foreach (var bytes in malformed)
        {
            using var input = new MemoryStream(bytes);
            Throws<UnsupportedSaveException>(() => codec.Read(input));
        }
        using var oversized = new MemoryStream(new byte[SaveCodec.MaximumFileBytes + 1]);
        Throws<UnsupportedSaveException>(() => codec.Read(oversized));
        foreach (var value in new[] { new string('x', SaveCodec.MaximumStringBytes + 1), "\ud800" })
        {
            var invalid = Save(10);
            invalid.SetString("note", value);
            using var output = new MemoryStream();
            Throws<UnsupportedSaveException>(() => codec.Write(output, invalid));
            Check(output.Length == 0, "invalid strings rejected before serialization starts");
        }
        var unicode = Save(10);
        unicode.SetString("note", "简體中文 / English / 🙂 / \0");
        using var roundTrip = new MemoryStream();
        codec.Write(roundTrip, unicode);
        var original = roundTrip.ToArray();
        roundTrip.Position = 0;
        var parsed = codec.Read(roundTrip);
        using var again = new MemoryStream();
        codec.Write(again, parsed);
        Check(original.SequenceEqual(again.ToArray()), "strict UTF8 preserves valid Unicode and NUL byte-for-byte");
    }

    private static void BoundedFileIo(string root)
    {
        var path = Path.Combine(root, "oversized.bin");
        using (var stream = File.Create(path)) stream.SetLength(SaveCodec.MaximumFileBytes + 1L);
        Throws<IOException>(() => BoundedSaveFile.Read(path));
        Throws<IOException>(() => SaveTargetState.Capture(path));
        Check(new FileInfo(path).Length == SaveCodec.MaximumFileBytes + 1L, "oversized bytes remain unchanged");
    }

    private static void WarningReadOnly(string root)
    {
        var path = Path.Combine(root, "save.bin");
        Write(path, 10);
        var original = File.ReadAllBytes(path);
        var warning = Save(20);
        warning.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 20));
        var validation = new SaveValidator().Validate(warning);
        Check(validation.IsValid && validation.HasWarnings && !validation.CanWrite, "warnings are not writable");
        var store = new BackupService(Path.Combine(root, "backups"));
        Throws<UnsupportedSaveException>(() => new AtomicSaveService().Save(path, warning, store.BackupDirectory));
        Check(File.ReadAllBytes(path).SequenceEqual(original), "warning save cannot overwrite a destination");
        var warningFile = Path.Combine(root, "warning.bin");
        using (var output = File.Create(warningFile)) new SaveCodec().Write(output, warning);
        var backup = store.CreateSnapshot(warningFile, "open")!;
        Throws<UnsupportedSaveException>(() => store.Restore(backup.Path, path));
        Check(File.ReadAllBytes(path).SequenceEqual(original), "warning backup cannot bypass write restrictions through restore");
        Check(!new SaveValidator().Validate(new CutTheRopeSave()).CanWrite, "unrecognized empty containers are view-only");
        var snapshot = store.CreateSnapshot(path, "open")!;
        Throws<IOException>(() => new AtomicSaveService().Save(snapshot.Path, Save(30), store.BackupDirectory));
        Check(File.ReadAllBytes(snapshot.Path).SequenceEqual(original), "backup store is not a save destination");
    }

    private static void StaleDestination(string root)
    {
        var path = Path.Combine(root, "save.bin");
        Write(path, 10);
        var expected = SaveTargetState.Capture(path);
        Write(path, 20);
        Throws<SaveTargetChangedException>(() => new AtomicSaveService().Save(path, Save(30), Path.Combine(root, "backups"), expectedTarget: expected));
        Check(Score(path) == 20, "a changed opened file is never overwritten");
        var store = new BackupService(Path.Combine(root, "backups"));
        var snapshot = store.CreateSnapshot(path, "open")!;
        Write(path, 40);
        Throws<SaveTargetChangedException>(() => store.Restore(snapshot.Path, path, expectedTarget: expected));
        Check(Score(path) == 40, "restore also honors the reviewed destination state");
    }

    private static void PrecommitChanges(string root)
    {
        var path = Path.Combine(root, "save.bin");
        foreach (var stage in new[] { SaveWriteStage.TempValidated, SaveWriteStage.BeforeReplace })
        {
            Write(path, 10);
            var service = new AtomicSaveService(at => { if (at == stage) Write(path, 20); });
            Throws<SaveTargetChangedException>(() => service.Save(path, Save(30), Path.Combine(root, "backups")));
            Check(Score(path) == 20, "detected precommit change survives failure without rollback");
        }
        File.Delete(path);
        var created = new AtomicSaveService(at => { if (at == SaveWriteStage.TempValidated) Write(path, 40); });
        Throws<SaveTargetChangedException>(() => created.Save(path, Save(30), Path.Combine(root, "backups")));
        Check(Score(path) == 40, "newly appeared file is not overwritten");
        var deleted = new AtomicSaveService(at => { if (at == SaveWriteStage.TempValidated) File.Delete(path); });
        Throws<SaveTargetChangedException>(() => deleted.Save(path, Save(30), Path.Combine(root, "backups")));
        Check(!File.Exists(path), "a removed file is not unexpectedly recreated");
    }

    private static void PostcommitChanges(string root)
    {
        var path = Path.Combine(root, "save.bin");
        Write(path, 10);
        var service = new AtomicSaveService(at => { if (at == SaveWriteStage.AfterReplace) Write(path, 40); });
        var failure = Throws<SaveWriteFailureException>(() => service.Save(path, Save(30), Path.Combine(root, "backups")));
        Check(failure.Outcome == SaveRecoveryOutcome.CurrentFileRetained && Score(path) == 40, "postcommit external update is retained, not blindly rolled back");
        Check(failure.RecoveryPaths.Any(p => File.Exists(p) && Score(p) == 10), "original remains recoverable after external postcommit update");
        ResolveKeepingCurrent(Path.Combine(root, "backups"));
        File.Delete(path);
        var newFile = new AtomicSaveService(at => { if (at == SaveWriteStage.AfterReplace) throw new IOException("readback fault"); });
        failure = Throws<SaveWriteFailureException>(() => newFile.Save(path, Save(30), Path.Combine(root, "backups")));
        Check(failure.Outcome == SaveRecoveryOutcome.CurrentFileRetained && Score(path) == 30, "uncertain new output is kept, not silently deleted");
    }

    private static void RollbackFailures(string root)
    {
        var path = Path.Combine(root, "save.bin");
        foreach (var failRecovery in new[] { false, true })
        {
            Write(path, 10);
            var service = new AtomicSaveService(at =>
            {
                if (at == SaveWriteStage.AfterReplace || failRecovery && at == SaveWriteStage.BeforeRollback)
                    throw new IOException("injected readback/recovery failure");
            });
            var failure = Throws<SaveWriteFailureException>(() => service.Save(path, Save(30), Path.Combine(root, "backups")));
            Check(failure.Outcome == (failRecovery ? SaveRecoveryOutcome.RecoveryNeedsReview : SaveRecoveryOutcome.OriginalRestored), "recovery status reflects actual outcome");
            Check(Score(path) == (failRecovery ? 30 : 10), "successful rollback restores actual prior bytes");
            Check(failure.RecoveryPaths.Any(p => File.Exists(p) && Score(p) == 10), "prior save remains recoverable even when rollback fails");
            ResolveKeepingCurrent(Path.Combine(root, "backups"));
        }
    }

    private static void CommitRenameRace(string root)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(root, "save.bin");
        var external = Path.Combine(root, "external.bin");
        Write(path, 10);
        Write(external, 20);
        var service = new AtomicSaveService(at =>
        {
            if (at == SaveWriteStage.CommitBoundary) File.Replace(external, path, null);
        });
        var failure = Throws<SaveWriteFailureException>(() => service.Save(path, Save(30), Path.Combine(root, "backups")));
        Check(failure.Outcome == SaveRecoveryOutcome.OriginalRestored && Score(path) == 20, "atomic displacement detects and recovers the file actually replaced during a rename race");
    }

    private static void CleanupFailure(string root)
    {
        var path = Path.Combine(root, "save.bin");
        Write(path, 10);
        FileStream? held = null;
        try
        {
            var service = new AtomicSaveService(at =>
            {
                if (at == SaveWriteStage.BeforeCleanup)
                    held = File.OpenRead(Directory.GetFiles(root, "*.cts-recovery").Single());
            });
            var result = service.Save(path, Save(30), Path.Combine(root, "backups"));
            Check(Score(path) == 30, "cleanup failure does not reclassify a verified save");
            if (OperatingSystem.IsWindows())
                Check(result.RetainedPaths is { Count: 2 } && result.RetainedPaths.All(File.Exists)
                    && result.RetainedPaths.Any(p => p.EndsWith("save-transaction.json", StringComparison.Ordinal)),
                    "in-use recovery artifact and durable journal are both reported and retained");
        }
        finally { held?.Dispose(); }
    }

    private static void NativeCommitFailure(string root)
    {
        var path = Path.Combine(root, "save.bin");
        var service = new AtomicSaveService(at =>
        {
            if (at == SaveWriteStage.CommitBoundary) Write(path, 40);
        });
        var failure = Throws<SaveWriteFailureException>(() => service.Save(path, Save(30), Path.Combine(root, "backups")));
        Check(failure.Outcome == SaveRecoveryOutcome.CurrentFileRetained && Score(path) == 40,
            "failed native create reports uncertainty without removing the file that appeared at commit");
    }

    private static void BusyWriter(string root)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(root, "save.bin");
        Write(path, 10);
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            Throws<IOException>(() => new AtomicSaveService().Save(path, Save(30), Path.Combine(root, "backups")));
        Check(Score(path) == 10, "an existing in-place writer blocks the save without changing bytes");
        var service = new AtomicSaveService(at =>
        {
            if (at == SaveWriteStage.CommitBoundary) Throws<IOException>(() => Write(path, 20));
        });
        service.Save(path, Save(30), Path.Combine(root, "backups"));
        Check(Score(path) == 30, "commit read lease blocks a competing in-place writer");
    }

    private static void TargetCoordination(string root)
    {
        var path = Path.Combine(root, "save.bin");
        Write(path, 10);
        var service = new AtomicSaveService(at =>
        {
            if (at != SaveWriteStage.TempValidated) return;
            Task.Run(() => Throws<IOException>(() => new AtomicSaveService().Save(path, Save(20), Path.Combine(root, "other-backups")))).GetAwaiter().GetResult();
        });
        service.Save(path, Save(30), Path.Combine(root, "backups"));
        Check(Score(path) == 30, "target mutex coordinates separate stores using the same destination");
    }

    private static CutTheRopeSave Save(int score) { var save = new CutTheRopeSave(); save.SetInt("SCORE_0_0", score); return save; }
    private static void ResolveKeepingCurrent(string directory)
    {
        var store = new BackupService(directory);
        store.ResolveInterruptedSave(store.InspectInterruptedSave(), restorePrevious: false);
    }
    private static void Write(string path, int score) { using var output = File.Create(path); new SaveCodec().Write(output, Save(score)); }
    private static int Score(string path) { using var input = File.OpenRead(path); var save = new SaveCodec().Read(input); return save.IntEntries.Single().Value; }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T exception) { return exception; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Fixture(Action<string> test)
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-write-safety-");
        try { test(root.FullName); }
        catch (Exception exception) { throw new InvalidOperationException(test.Method.Name + ": " + exception, exception); }
        finally { if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true); }
    }
}
