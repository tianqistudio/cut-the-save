using System.Security.Cryptography;

namespace CutTheSave.Core;

public sealed record SaveSnapshot(string Path, long Length, string Sha256, IReadOnlyList<string>? RetainedPaths = null);
public enum SaveRecoveryOutcome { OriginalRestored, CurrentFileRetained, RecoveryNeedsReview }
public sealed class SaveWriteFailureException(
    Exception cause, SaveRecoveryOutcome outcome, IReadOnlyList<string> recoveryPaths)
    : IOException("Final save verification failed. Recovery outcome: " + outcome + ". Keep the recovery files and inspect the destination.", cause)
{
    public SaveRecoveryOutcome Outcome { get; } = outcome;
    public IReadOnlyList<string> RecoveryPaths { get; } = recoveryPaths;
}
internal enum SaveWriteStage { IntentFlushed, TempValidated, BeforeReplace, CommitBoundary, AfterReplace, BeforeRollback, AfterRollback, BeforeCleanup }

public sealed class AtomicSaveService
{
    private readonly SaveCodec _codec;
    private readonly SaveValidator _validator;
    private readonly Action<SaveWriteStage>? _observe;

    public AtomicSaveService(SaveCodec? codec = null, SaveValidator? validator = null)
    {
        _codec = codec ?? new SaveCodec();
        _validator = validator ?? new SaveValidator();
    }

    internal AtomicSaveService(Action<SaveWriteStage> observe) : this() => _observe = observe;

    public SaveSnapshot Save(string targetPath, CutTheRopeSave save, string backupDirectory,
        string backupReason = "pre-write", SaveTargetState? expectedTarget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        if (!_validator.Validate(save).CanWrite)
            throw new UnsupportedSaveException("The save has errors or unresolved warnings and is read-only.");

        var target = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(target) ?? throw new ArgumentException("No destination directory.");
        var storePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        var relativeToStore = Path.GetRelativePath(storePath, target);
        if (relativeToStore != ".." && !relativeToStore.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relativeToStore))
            throw new IOException("A save destination must not overwrite files inside the backup store.");
        using var targetLease = SaveTargetState.BeginWrite(target);
        var expected = expectedTarget ?? SaveTargetState.Capture(target);
        expected.EnsureCurrent(target);
        Directory.CreateDirectory(directory);
        var backups = new BackupService(storePath, _codec, _validator);
        using var storeLease = backups.BeginTransaction();
        backups.EnsureRetentionStable();
        backups.EnsureSaveRecoveryStable();
        // Validate the history even when this is a brand-new destination.
        backups.List();
        Directory.CreateDirectory(storePath);

        var token = Guid.NewGuid().ToString("N");
        var temp = target + "." + token + ".cts.tmp";
        // Same-volume atomic displacement preserves the actual bytes replaced at commit.
        var displaced = target + "." + token + ".cts-recovery";
        var rollbackDisplaced = target + "." + token + ".cts-recovery-current";
        var retained = new List<string>();
        string? prewrite = null;
        byte[]? written = null;
        byte[]? intentBytes = null;
        var replaced = false;
        var commitStarted = false;
        var success = false;
        try
        {
            using (var serialized = new MemoryStream())
            {
                _codec.Write(serialized, save);
                written = serialized.ToArray();
            }
            ValidateExact(written);
            expected.EnsureCurrent(target);
            BackupRecord? prewriteRecord = null;
            if (expected.Exists)
            {
                prewriteRecord = backups.CreateSnapshot(target, backupReason) ?? throw new SaveTargetChangedException();
                prewrite = prewriteRecord.Path;
                if (!Matches(prewrite, expected)) throw new SaveTargetChangedException();
            }
            intentBytes = backups.BeginSaveIntent(new SaveWriteIntent(1, token, expected,
                SaveTargetState.FromBytes(target, written), prewriteRecord));
            _observe?.Invoke(SaveWriteStage.IntentFlushed);
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.WriteThrough))
            {
                output.Write(written);
                output.Flush(true);
            }
            var staged = BoundedSaveFile.Read(temp);
            if (!staged.AsSpan().SequenceEqual(written)) throw new IOException("The staged save changed before commit.");
            ValidateExact(staged);
            _observe?.Invoke(SaveWriteStage.TempValidated);
            expected.EnsureCurrent(target);
            _observe?.Invoke(SaveWriteStage.BeforeReplace);
            expected.EnsureCurrent(target);
            // Deny in-place writers while allowing the native atomic replacement. A rename
            // race is detected by the displaced bytes, which are kept for recovery.
            using (var guard = expected.Exists
                ? new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete) : null)
            {
                expected.EnsureCurrent(target);
                _observe?.Invoke(SaveWriteStage.CommitBoundary);
                commitStarted = true;
                if (expected.Exists) File.Replace(temp, target, displaced);
                else File.Move(temp, target, overwrite: false);
                replaced = true;
            }
            _observe?.Invoke(SaveWriteStage.AfterReplace);
            if (expected.Exists && !Matches(displaced, expected)) throw new SaveTargetChangedException();
            var final = BoundedSaveFile.Read(target);
            if (!final.AsSpan().SequenceEqual(written)) throw new IOException("The final file differs from the validated output.");
            ValidateExact(final);
            success = true;
            return new SaveSnapshot(target, final.LongLength, Hash(final), retained);
        }
        catch (Exception exception) when (replaced || commitStarted)
        {
            var outcome = SaveRecoveryOutcome.CurrentFileRetained;
            if (prewrite is not null) retained.Add(prewrite);
            if (File.Exists(displaced)) retained.Add(displaced);
            try
            {
                // Never copy a backup over a changed/unknown destination. The displaced
                // file is the actual prior state, including a rename race at commit.
                if (expected.Exists && written is not null && File.Exists(displaced)
                    && BoundedSaveFile.Read(target).AsSpan().SequenceEqual(written))
                {
                    _observe?.Invoke(SaveWriteStage.BeforeRollback);
                    using var guard = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    if (!BoundedSaveFile.Read(target).AsSpan().SequenceEqual(written))
                        throw new SaveTargetChangedException();
                    var prior = BoundedSaveFile.Read(displaced);
                    File.Replace(displaced, target, rollbackDisplaced);
                    _observe?.Invoke(SaveWriteStage.AfterRollback);
                    retained.Remove(displaced);
                    retained.Add(rollbackDisplaced);
                    if (!BoundedSaveFile.Read(rollbackDisplaced).AsSpan().SequenceEqual(written)
                        || !BoundedSaveFile.Read(target).AsSpan().SequenceEqual(prior))
                        throw new IOException("The destination changed during recovery; displaced content was retained.");
                    outcome = SaveRecoveryOutcome.OriginalRestored;
                }
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
            {
                outcome = SaveRecoveryOutcome.RecoveryNeedsReview;
                if (File.Exists(rollbackDisplaced) && !retained.Contains(rollbackDisplaced)) retained.Add(rollbackDisplaced);
            }
            throw new SaveWriteFailureException(exception, outcome, retained);
        }
        finally
        {
            try { _observe?.Invoke(SaveWriteStage.BeforeCleanup); }
            catch (IOException) { /* A cleanup fault must not change the save result. */ }
            TryRemoveOwned(temp, written, retained);
            if (success) TryRemoveOwned(displaced, expected.Exists ? expected : null, retained);
            if (intentBytes is not null) backups.FinishSaveIntent(intentBytes,
                (success || !commitStarted) && retained.Count == 0, retained);
        }
    }

    private void ValidateExact(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, false);
        var parsed = _codec.Read(input);
        if (!_validator.Validate(parsed).CanWrite) throw new UnsupportedSaveException("The serialized save has unresolved validation issues.");
        using var check = new MemoryStream();
        _codec.Write(check, parsed);
        if (!bytes.AsSpan().SequenceEqual(check.ToArray()))
            throw new UnsupportedSaveException("The save cannot be rewritten byte-for-byte.");
    }

    private static bool Matches(string path, SaveTargetState expected)
    {
        var actual = SaveTargetState.Capture(path);
        return actual.Exists && actual.Length == expected.Length
            && string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryRemoveOwned(string path, byte[]? expectedBytes, List<string> retained) =>
        TryRemoveOwned(path, expectedBytes is null ? null : SaveTargetState.FromBytes(path, expectedBytes), retained);

    private static void TryRemoveOwned(string path, SaveTargetState? expected, List<string> retained)
    {
        if (!File.Exists(path)) return;
        try
        {
            if (expected is not null && !Matches(path, expected)) throw new IOException("A transaction artifact changed.");
            File.Delete(path);
            if (File.Exists(path)) throw new IOException("The transaction artifact remains in use.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!retained.Contains(path)) retained.Add(path);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
