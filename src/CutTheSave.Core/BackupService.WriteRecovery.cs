using System.Text.Json;

namespace CutTheSave.Core;

public enum InterruptedSaveState { None, OriginalPresent, OutputPresent, Changed, NeedsReview }
public sealed record InterruptedSaveStatus(InterruptedSaveState State, string JournalPath,
    string? JournalSha256 = null, SaveTargetState? CurrentTarget = null, bool CanKeepCurrent = false,
    bool CanRestorePrevious = false, IReadOnlyList<string>? RetainedPaths = null, string? Detail = null);
public sealed record InterruptedSaveResolution(string TargetPath, bool RestoredPrevious,
    string ArchivePath, IReadOnlyList<string> RetainedPaths);
public sealed class SaveRecoveryPendingException(string journalPath)
    : IOException("A save was interrupted. Open Backups to review it before writing again.")
{
    public string JournalPath { get; } = journalPath;
}
internal sealed record SaveWriteIntent(int Version, string Token, SaveTargetState Before,
    SaveTargetState Output, BackupRecord? Prewrite);
internal enum SaveRestartStage { AfterRestore, BeforeArchive }

public sealed partial class BackupService
{
    private string SaveJournalPath => Path.Combine(BackupDirectory, "save-transaction.json");

    internal void EnsureSaveRecoveryStable()
    {
        EnsureDirectStoreFile(SaveJournalPath);
        if (File.Exists(SaveJournalPath)) throw new SaveRecoveryPendingException(SaveJournalPath);
    }

    internal byte[] BeginSaveIntent(SaveWriteIntent intent)
    {
        EnsureSaveRecoveryStable();
        EnsureStoreDirectory();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(intent, _jsonOptions);
        WriteNewFlushedFile(SaveJournalPath, bytes);
        return bytes;
    }

    internal void FinishSaveIntent(byte[] intentBytes, bool mayClear, List<string> retained)
    {
        try
        {
            if (!mayClear) throw new IOException("Save recovery review remains pending.");
            EnsureDirectStoreFile(SaveJournalPath);
            using var guard = new FileStream(SaveJournalPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (!ReadBoundedBytes(SaveJournalPath).AsSpan().SequenceEqual(intentBytes))
                throw new IOException("The save journal changed.");
            File.Delete(SaveJournalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!retained.Contains(SaveJournalPath)) retained.Add(SaveJournalPath);
        }
    }

    public InterruptedSaveStatus InspectInterruptedSave()
    {
        using var lease = BeginTransaction();
        try
        {
            EnsureDirectStoreFile(SaveJournalPath);
            if (!File.Exists(SaveJournalPath)) return new(InterruptedSaveState.None, SaveJournalPath);
            var (intent, bytes) = ReadSaveIntent();
            var current = SaveTargetState.Capture(intent.Before.Path);
            var changedArtifacts = new List<string>();
            foreach (var (path, expected) in IntentArtifacts(intent))
            {
                try
                {
                    RejectLinks(path);
                    if (Directory.Exists(path)) throw new IOException("Recovery artifact is a directory.");
                    if (File.Exists(path) && !SameContent(SaveTargetState.Capture(path), expected)) changedArtifacts.Add(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { changedArtifacts.Add(path); }
            }
            var state = SameContent(current, intent.Output) ? InterruptedSaveState.OutputPresent
                : SameContent(current, intent.Before) ? InterruptedSaveState.OriginalPresent : InterruptedSaveState.Changed;
            return new(state, SaveJournalPath, ComputeSha256(bytes), current, CanKeepCurrent: true,
                CanRestorePrevious: state == InterruptedSaveState.OutputPresent && intent.Before.Exists && changedArtifacts.Count == 0,
                RetainedPaths: changedArtifacts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return new(InterruptedSaveState.NeedsReview, SaveJournalPath, Detail: exception.Message);
        }
    }

    public InterruptedSaveResolution ResolveInterruptedSave(InterruptedSaveStatus reviewed, bool restorePrevious) =>
        ResolveInterruptedSave(reviewed, restorePrevious, null);

    internal InterruptedSaveResolution ResolveInterruptedSave(InterruptedSaveStatus reviewed, bool restorePrevious,
        Action<SaveRestartStage>? observe)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        using var storeLease = BeginTransaction();
        EnsureRetentionStable();
        var current = InspectInterruptedSave();
        if (!current.CanKeepCurrent || reviewed.CurrentTarget is null || current.CurrentTarget is null
            || !SaveTargetState.SamePath(reviewed.JournalPath, SaveJournalPath)
            || reviewed.JournalSha256 != current.JournalSha256
            || !SaveTargetState.SamePath(reviewed.CurrentTarget.Path, current.CurrentTarget.Path)
            || !SameContent(reviewed.CurrentTarget, current.CurrentTarget)) throw new SaveTargetChangedException();
        using var targetLease = SaveTargetState.BeginWrite(current.CurrentTarget.Path);
        var (intent, intentBytes) = ReadSaveIntent();
        if (restorePrevious && (!current.CanRestorePrevious || intent.Prewrite is null))
            throw new IOException("Only an unchanged, verified output can be replaced by its verified previous snapshot.");
        // Keep the original indexed snapshot permanently protected when resolving an interruption.
        if (intent.Prewrite is not null)
        {
            var records = LoadRecords();
            var index = FindRecord(records, intent.Prewrite.Path);
            ReadVerifiedBytes(records[index]);
            if (!records[index].IsLocked)
            {
                records[index] = records[index] with { IsLocked = true };
                SaveRecords(records);
            }
        }
        current.CurrentTarget.EnsureCurrent(intent.Before.Path);
        var accepted = current.CurrentTarget;
        if (restorePrevious)
        {
            if (!current.CanRestorePrevious || intent.Prewrite is null)
                throw new IOException("Only an unchanged, verified output can be replaced by its verified previous snapshot.");
            var restoreTemp = ArtifactPath(intent, ".cts-restore");
            var restoredOutput = ArtifactPath(intent, ".cts-restart-displaced");
            if (File.Exists(restoredOutput) || Directory.Exists(restoredOutput))
                throw new IOException("An earlier recovery output already exists; it was retained.");
            var currentBackup = CreateSnapshotCore(intent.Before.Path, "recovery-pre-restore", true,
                "Verified output before explicit interrupted-save recovery.") ?? throw new SaveTargetChangedException();
            if (currentBackup.Sha256 != intent.Output.Sha256 || currentBackup.FileSize != intent.Output.Length)
                throw new SaveTargetChangedException();
            var outputRecords = LoadRecords();
            var outputIndex = FindRecord(outputRecords, currentBackup.Path);
            if (!outputRecords[outputIndex].IsLocked)
            {
                outputRecords[outputIndex] = outputRecords[outputIndex] with { IsLocked = true };
                SaveRecords(outputRecords);
            }
            if (!File.Exists(restoreTemp)) WriteNewFlushedFile(restoreTemp, ReadVerifiedBytes(intent.Prewrite));
            RejectLinks(restoreTemp);
            if (!SameContent(SaveTargetState.Capture(restoreTemp), intent.Before)) throw new IOException("The recovery copy changed.");
            using (var guard = new FileStream(intent.Before.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                current.CurrentTarget.EnsureCurrent(intent.Before.Path);
                File.Replace(restoreTemp, intent.Before.Path, restoredOutput);
            }
            observe?.Invoke(SaveRestartStage.AfterRestore);
            if (!SameContent(SaveTargetState.Capture(restoredOutput), intent.Output)
                || !SameContent(SaveTargetState.Capture(intent.Before.Path), intent.Before))
                throw new IOException("The file changed during recovery. All recovery records were retained.");
            accepted = intent.Before;
        }

        var retained = new List<string>();
        foreach (var (path, expected) in IntentArtifacts(intent))
        {
            try
            {
                RejectLinks(path);
                if (Directory.Exists(path)) throw new IOException("Recovery artifact is a directory.");
                if (!File.Exists(path)) continue;
                using var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (!SameContent(SaveTargetState.Capture(path), expected)) throw new IOException("Recovery artifact changed.");
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { retained.Add(path); }
        }
        observe?.Invoke(SaveRestartStage.BeforeArchive);
        accepted.EnsureCurrent(intent.Before.Path);
        var archive = Path.Combine(BackupDirectory, "save-recovery-" + intent.Token + ".json");
        EnsureDirectStoreFile(archive);
        using (var guard = new FileStream(SaveJournalPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            if (!ReadBoundedBytes(SaveJournalPath).AsSpan().SequenceEqual(intentBytes)) throw new IOException("The save journal changed.");
            File.Move(SaveJournalPath, archive, overwrite: false);
        }
        return new(intent.Before.Path, restorePrevious, archive, retained);
    }

    private (SaveWriteIntent Intent, byte[] Bytes) ReadSaveIntent()
    {
        EnsureDirectStoreFile(SaveJournalPath);
        var bytes = ReadBoundedBytes(SaveJournalPath);
        var intent = JsonSerializer.Deserialize<SaveWriteIntent>(bytes, _jsonOptions) ?? throw new IOException("Empty save journal.");
        if (intent.Version != 1 || !Guid.TryParseExact(intent.Token, "N", out _)
            || !ValidState(intent.Before) || !ValidState(intent.Output) || !intent.Output.Exists
            || !SaveTargetState.SamePath(intent.Before.Path, intent.Output.Path)) throw new IOException("Invalid save journal.");
        var relative = Path.GetRelativePath(BackupDirectory, intent.Before.Path);
        if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative)) throw new IOException("Save recovery cannot overwrite its backup store.");
        RejectLinks(intent.Before.Path);
        if (Directory.Exists(intent.Before.Path)) throw new IOException("The save target is a directory.");
        if (intent.Before.Exists)
        {
            if (intent.Prewrite is not { } before || !SaveTargetState.SamePath(before.OriginalPath, intent.Before.Path)
                || before.FileSize != intent.Before.Length || before.Sha256 != intent.Before.Sha256)
                throw new IOException("The prior snapshot does not match the save journal.");
            var records = LoadRecords();
            var indexed = records[FindRecord(records, before.Path)];
            if (indexed.Sha256 != before.Sha256 || indexed.FileSize != before.FileSize
                || !SaveTargetState.SamePath(indexed.OriginalPath, before.OriginalPath)) throw new IOException("The prior snapshot index changed.");
            ReadVerifiedBytes(indexed);
        }
        else if (intent.Prewrite is not null) throw new IOException("Unexpected prior snapshot for a new target.");
        return (intent, bytes);
    }

    private static bool ValidState(SaveTargetState? state) => state is not null && Path.IsPathFullyQualified(state.Path)
        && state.Length >= 0 && state.Length <= SaveCodec.MaximumFileBytes
        && (state.Exists ? IsHash(state.Sha256) : state.Length == 0 && state.Sha256 is null);
    private static bool SameContent(SaveTargetState first, SaveTargetState second) => first.Exists == second.Exists
        && first.Length == second.Length && string.Equals(first.Sha256, second.Sha256, StringComparison.OrdinalIgnoreCase);
    private static string ArtifactPath(SaveWriteIntent intent, string suffix) => intent.Before.Path + "." + intent.Token + suffix;
    private static IEnumerable<(string Path, SaveTargetState Expected)> IntentArtifacts(SaveWriteIntent intent)
    {
        yield return (ArtifactPath(intent, ".cts.tmp"), intent.Output);
        yield return (ArtifactPath(intent, ".cts-recovery"), intent.Before);
        yield return (ArtifactPath(intent, ".cts-recovery-current"), intent.Output);
        yield return (ArtifactPath(intent, ".cts-restore"), intent.Before);
        yield return (ArtifactPath(intent, ".cts-restart-displaced"), intent.Output);
    }
}
