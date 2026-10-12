using System.Text.Json;

namespace CutTheSave.Core;

public enum BackupMaintenanceState { None, RollbackPending, FinalizePending, NeedsReview }
public sealed record BackupMaintenanceStatus(BackupMaintenanceState State, string JournalPath, string? Detail = null);
public sealed class BackupMaintenancePendingException(string journalPath, Exception? cause = null)
    : IOException("Backup cleanup was interrupted. Open Backups to repair its records before writing again.", cause)
{
    public string JournalPath { get; } = journalPath;
}
internal enum BackupRetentionStage { JournalCreated, SnapshotQuarantined, IndexCommitted, QuarantineDeleted }

public sealed partial class BackupService
{
    private string RetentionJournalPath => Path.Combine(BackupDirectory, "retention-transaction.json");
    private readonly Action<BackupRetentionStage>? _observeRetention;
    internal BackupService(string backupDirectory, Action<BackupRetentionStage> observe) : this(backupDirectory)
        => _observeRetention = observe;

    // One snapshot per transaction makes partial batch completion unambiguous after restart.
    private sealed record RetentionJournal(int Version, string Token, BackupRetentionCandidate Candidate,
        int RecordIndex, string BeforeHash, string AfterHash);
    private sealed record RecoveryInspection(BackupMaintenanceStatus Status, RetentionJournal? Journal,
        byte[]? JournalBytes);

    internal void EnsureRetentionStable()
    {
        EnsureDirectStoreFile(RetentionJournalPath);
        if (File.Exists(RetentionJournalPath)) throw new BackupMaintenancePendingException(RetentionJournalPath);
    }

    public BackupMaintenanceStatus InspectMaintenance()
    {
        using var lease = BeginTransaction();
        return InspectMaintenanceCore().Status;
    }

    // Explicit user action: never writes a game save and never scans filenames to infer ownership.
    public BackupMaintenanceStatus RecoverInterruptedRetention()
    {
        using var lease = BeginTransaction();
        EnsureSaveRecoveryStable();
        var inspection = InspectMaintenanceCore();
        if (inspection.Status.State is BackupMaintenanceState.None or BackupMaintenanceState.NeedsReview)
            return inspection.Status;
        var journal = inspection.Journal!;
        var original = journal.Candidate.Record.Path;
        var quarantine = QuarantinePath(journal);
        if (File.Exists(quarantine))
        {
            using var guard = OpenVerifiedQuarantine(journal);
            if (inspection.Status.State == BackupMaintenanceState.RollbackPending)
                File.Move(quarantine, original, overwrite: false);
            else
                File.Delete(quarantine);
        }
        // Re-inspect after the move/delete, so a partially completed recovery is repeatable.
        var final = InspectMaintenanceCore();
        if (final.Status.State != inspection.Status.State) return final.Status;
        RemoveVerifiedJournal(inspection.JournalBytes!);
        return new BackupMaintenanceStatus(BackupMaintenanceState.None, RetentionJournalPath);
    }

    private void RemoveWithRetentionJournal(List<BackupRecord> records, BackupRetentionCandidate candidate)
    {
        var index = records.FindIndex(record => record == candidate.Record);
        if (index < 0) throw new IOException("The snapshot record changed.");
        var remaining = records.Where((_, position) => position != index).ToList();
        var journal = new RetentionJournal(1, Guid.NewGuid().ToString("N"), candidate,
            index, RecordsHash(records), RecordsHash(remaining));
        var journalBytes = JsonSerializer.SerializeToUtf8Bytes(journal, _jsonOptions);
        var indexState = SaveTargetState.Capture(_indexPath);
        if (RecordsHash(LoadRecords()) != journal.BeforeHash)
            throw new IOException("The backup index changed before cleanup.");
        WriteNewFlushedFile(RetentionJournalPath, journalBytes);
        _observeRetention?.Invoke(BackupRetentionStage.JournalCreated);
        try
        {
            // Share.Delete permits our rename, but blocks ordinary in-place writers during verification.
            using (var guard = new FileStream(candidate.Record.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                ReadVerifiedBytes(candidate.Record);
                var info = new FileInfo(candidate.Record.Path);
                if (info.LastWriteTimeUtc != candidate.LastWriteTimeUtc || info.CreationTimeUtc != candidate.CreationTimeUtc)
                    throw new IOException("The snapshot changed before quarantine.");
                File.Move(candidate.Record.Path, QuarantinePath(journal), overwrite: false);
            }
            _observeRetention?.Invoke(BackupRetentionStage.SnapshotQuarantined);
            using (OpenVerifiedQuarantine(journal)) { }
            indexState.EnsureCurrent(_indexPath);
            SaveRecords(remaining);
            _observeRetention?.Invoke(BackupRetentionStage.IndexCommitted);
            if (RecordsHash(LoadRecords()) != journal.AfterHash)
                throw new IOException("The backup index did not match the committed cleanup.");
            using (OpenVerifiedQuarantine(journal)) File.Delete(QuarantinePath(journal));
            _observeRetention?.Invoke(BackupRetentionStage.QuarantineDeleted);
            RemoveVerifiedJournal(journalBytes);
            records.RemoveAt(index);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A busy source which was never moved can be retried normally. Anything later
            // retains the journal for explicit, independently revalidated recovery.
            var inspection = InspectMaintenanceCore();
            if (inspection.Status.State == BackupMaintenanceState.RollbackPending
                && File.Exists(candidate.Record.Path) && !File.Exists(QuarantinePath(journal)))
            {
                RemoveVerifiedJournal(journalBytes);
                throw;
            }
            throw new BackupMaintenancePendingException(RetentionJournalPath, exception);
        }
    }

    private RecoveryInspection InspectMaintenanceCore()
    {
        BackupMaintenanceStatus Status(BackupMaintenanceState state, string? detail = null) => new(state, RetentionJournalPath, detail);
        try
        {
            EnsureDirectStoreFile(RetentionJournalPath);
            if (!File.Exists(RetentionJournalPath)) return new(Status(BackupMaintenanceState.None), null, null);
            var bytes = ReadBoundedBytes(RetentionJournalPath);
            var journal = JsonSerializer.Deserialize<RetentionJournal>(bytes, _jsonOptions)
                ?? throw new IOException("Empty cleanup journal.");
            if (journal.Version != 1 || !Guid.TryParseExact(journal.Token, "N", out _)
                || journal.Candidate?.Record is not { } record || journal.RecordIndex < 0
                || !IsHash(journal.BeforeHash) || !IsHash(journal.AfterHash)
                || !IsHash(record.Sha256) || record.FileSize < 0 || record.FileSize > SaveCodec.MaximumFileBytes
                || !record.RetentionEligible || record.IsLocked || !Path.IsPathFullyQualified(record.Path)
                || !HasManagedName(record.Path)) throw new IOException("Invalid cleanup journal.");
            EnsureDirectStoreFile(record.Path);
            var quarantine = QuarantinePath(journal);
            EnsureDirectStoreFile(quarantine);
            var records = LoadRecords();
            var hash = RecordsHash(records);
            BackupMaintenanceState state;
            if (hash == journal.BeforeHash)
            {
                if (journal.RecordIndex >= records.Count || records[journal.RecordIndex] != record)
                    throw new IOException("Cleanup record no longer matches the index.");
                records.RemoveAt(journal.RecordIndex);
                if (RecordsHash(records) != journal.AfterHash) throw new IOException("Invalid cleanup transition.");
                state = BackupMaintenanceState.RollbackPending;
            }
            else if (hash == journal.AfterHash)
            {
                if (journal.RecordIndex > records.Count || records.Any(item => PathComparer.Equals(item.Path, record.Path)))
                    throw new IOException("Cleanup destination conflicts with the index.");
                records.Insert(journal.RecordIndex, record);
                if (RecordsHash(records) != journal.BeforeHash) throw new IOException("Invalid cleanup transition.");
                state = BackupMaintenanceState.FinalizePending;
            }
            else throw new IOException("The backup index changed outside this cleanup transaction.");

            var originalExists = File.Exists(record.Path);
            var quarantineExists = File.Exists(quarantine);
            if (state == BackupMaintenanceState.RollbackPending ? originalExists == quarantineExists : originalExists)
                throw new IOException("Cleanup files are missing or conflicting. Keep them for review.");
            if (originalExists) ReadVerifiedBytes(record);
            if (quarantineExists) using (OpenVerifiedQuarantine(journal)) { }
            return new(Status(state), journal, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            return new(Status(BackupMaintenanceState.NeedsReview, exception.Message), null, null);
        }
    }

    private FileStream OpenVerifiedQuarantine(RetentionJournal journal)
    {
        var path = QuarantinePath(journal);
        EnsureDirectStoreFile(path);
        var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try { ReadVerifiedBytes(journal.Candidate.Record with { Path = path }); return guard; }
        catch { guard.Dispose(); throw; }
    }

    private void RemoveVerifiedJournal(byte[] expected)
    {
        EnsureDirectStoreFile(RetentionJournalPath);
        using var guard = new FileStream(RetentionJournalPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (!ReadBoundedBytes(RetentionJournalPath).AsSpan().SequenceEqual(expected))
            throw new IOException("The cleanup journal changed; it was retained.");
        File.Delete(RetentionJournalPath);
    }

    private string QuarantinePath(RetentionJournal journal) => Path.Combine(BackupDirectory, "cts-retention-" + journal.Token + ".quarantine");
    private string RecordsHash(List<BackupRecord> records) => ComputeSha256(JsonSerializer.SerializeToUtf8Bytes(records, _jsonOptions));
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
