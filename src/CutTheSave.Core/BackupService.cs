using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CutTheSave.Core;

public sealed record BackupRecord(
    string Path, string OriginalPath, DateTimeOffset CreatedAt, string Reason,
    string Sha256, long FileSize, string? Note = null, bool IsLocked = false,
    bool RetentionEligible = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string>? RetainedTransactionPaths { get; init; }
}

public sealed partial class BackupService
{
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly SaveCodec _codec;
    private readonly SaveValidator _validator;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public BackupService(string backupDirectory, SaveCodec? codec = null, SaveValidator? validator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        BackupDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        _indexPath = Path.Combine(BackupDirectory, "backup-index.json");
        _codec = codec ?? new SaveCodec();
        _validator = validator ?? new SaveValidator();
    }

    public string BackupDirectory { get; }

    // A named mutex also coordinates separate app instances using the same store.
    internal IDisposable BeginTransaction()
    {
        var identity = OperatingSystem.IsWindows() ? BackupDirectory.ToUpperInvariant() : BackupDirectory;
        var name = "CutTheSave.Backups." + ComputeSha256(Encoding.UTF8.GetBytes(identity));
        var mutex = new Mutex(false, name);
        try
        {
            try
            {
                if (!mutex.WaitOne(0))
                    throw new IOException("The backup store is busy in another operation. Try again.");
            }
            catch (AbandonedMutexException) { /* Always re-read and validate the index. */ }
            return new StoreLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    public BackupRecord? CreateSnapshot(string originalPath, string reason, bool deduplicateByHash = false, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var lease = BeginTransaction();
        var fullOriginalPath = Path.GetFullPath(originalPath);
        EnsureRetentionStable();
        EnsureSaveRecoveryStable();
        return CreateSnapshotCore(fullOriginalPath, reason, deduplicateByHash, note);
    }

    // Caller must hold the store lease; restart recovery uses this only after reviewing
    // the journal and target, so its pre-restore snapshot does not bypass public guards.
    private BackupRecord? CreateSnapshotCore(string fullOriginalPath, string reason, bool deduplicateByHash, string? note)
    {
        if (!File.Exists(fullOriginalPath))
            return null;

        EnsureStoreDirectory();
        var records = LoadRecords();
        var bytes = ReadBoundedBytes(fullOriginalPath);
        var hash = ComputeSha256(bytes);
        var now = DateTimeOffset.UtcNow;
        if (deduplicateByHash)
        {
            foreach (var existing in records.Where(record => PathComparer.Equals(record.OriginalPath, fullOriginalPath)
                         && record.CreatedAt.UtcDateTime.Date == now.UtcDateTime.Date
                         && string.Equals(record.Sha256, hash, StringComparison.OrdinalIgnoreCase)).Reverse())
            {
                try { ReadVerifiedBytes(existing); return existing; }
                catch (IOException) { /* A missing or changed snapshot is not a valid duplicate. */ }
            }
        }

        var baseName = Path.GetFileName(fullOriginalPath);
        if (baseName.Length > 80) baseName = baseName[..80];
        var safeReason = new string(reason.Where(char.IsAsciiLetterOrDigit).Take(32).ToArray());
        var backupPath = Path.Combine(BackupDirectory, $"cts-v1-{Guid.NewGuid():N}_{safeReason}_{baseName}");
        WriteNewFlushedFile(backupPath, bytes);
        if (ComputeSha256(File.ReadAllBytes(backupPath)) != hash)
            throw new IOException("The snapshot did not match its source after writing.");

        var record = new BackupRecord(backupPath, fullOriginalPath, now, reason, hash, bytes.LongLength,
            note, IsLocked: false, RetentionEligible: true);
        records.Add(record);
        // If indexing fails, retain the verified file for manual recovery. Do not reset the index.
        SaveRecords(records);
        return record;
    }

    public IReadOnlyList<BackupRecord> List(string? originalPath = null)
    {
        using var lease = BeginTransaction();
        var records = LoadRecords();
        if (string.IsNullOrWhiteSpace(originalPath))
            return records;
        var fullOriginalPath = Path.GetFullPath(originalPath);
        return records.Where(record => PathComparer.Equals(record.OriginalPath, fullOriginalPath)).ToArray();
    }

    public BackupRecord SetLocked(string backupPath, bool isLocked)
    {
        using var lease = BeginTransaction();
        EnsureRetentionStable();
        EnsureSaveRecoveryStable();
        var records = LoadRecords();
        var index = FindRecord(records, backupPath);
        ReadVerifiedBytes(records[index]);
        var updated = records[index] with { IsLocked = isLocked };
        records[index] = updated;
        SaveRecords(records);
        return updated;
    }

    public BackupRecord Verify(string backupPath)
    {
        using var lease = BeginTransaction();
        var records = LoadRecords();
        var record = records[FindRecord(records, backupPath)];
        ReadVerifiedBytes(record);
        return record;
    }

    public IDisposable ProtectSnapshot(string backupPath)
    {
        using var lease = BeginTransaction();
        var records = LoadRecords();
        var record = records[FindRecord(records, backupPath)];
        ReadVerifiedBytes(record);
        // On Windows this read handle prevents deletion or replacement by other app
        // instances too. Closing the editor releases it without changing the lock flag.
        return new FileStream(record.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public BackupRecord Restore(string backupPath, string targetPath, string reason = "recovery-pre-restore", SaveTargetState? expectedTarget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        using var lease = BeginTransaction();
        EnsureRetentionStable();
        EnsureSaveRecoveryStable();
        var fullBackupPath = Path.GetFullPath(backupPath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullBackupPath))
            throw new FileNotFoundException("The selected backup does not exist.", fullBackupPath);

        EnsureDirectStoreFile(fullBackupPath);
        var bytes = ReadBoundedBytes(fullBackupPath);
        ValidateSaveBytes(bytes, "The selected backup is not a valid save and was not restored.");
        var records = LoadRecords();
        var selected = records[FindRecord(records, fullBackupPath)];
        if (selected.FileSize != bytes.LongLength || !string.Equals(selected.Sha256, ComputeSha256(bytes), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected backup no longer matches its recorded hash.");

        // Uses the same indexed pre-write snapshot and rollback path as normal saving.
        using var input = new MemoryStream(bytes, writable: false);
        var save = _codec.Read(input);
        using var roundTrip = new MemoryStream();
        _codec.Write(roundTrip, save);
        if (!bytes.AsSpan().SequenceEqual(roundTrip.ToArray()))
            throw new UnsupportedSaveException("The backup cannot be restored byte-for-byte by this codec.");
        var output = new AtomicSaveService(_codec, _validator).Save(fullTargetPath, save, BackupDirectory, reason, expectedTarget);
        return selected with { OriginalPath = fullTargetPath, Reason = "restored", RetainedTransactionPaths = output.RetainedPaths };
    }

    private List<BackupRecord> LoadRecords()
    {
        if (!File.Exists(_indexPath))
        {
            if (Directory.Exists(_indexPath))
                throw new IOException("The backup index path is a directory.");
            return [];
        }
        EnsureDirectStoreFile(_indexPath);
        List<BackupRecord> records;
        try
        {
            records = JsonSerializer.Deserialize<List<BackupRecord>>(File.ReadAllText(_indexPath), _jsonOptions)
                ?? throw new IOException("The backup index is empty or invalid; it was not overwritten.");
        }
        catch (JsonException exception)
        {
            throw new IOException("The backup index is damaged; it was not reset or overwritten.", exception);
        }
        if (records.Any(record => record is null || string.IsNullOrWhiteSpace(record.Path)
                || string.IsNullOrWhiteSpace(record.OriginalPath) || string.IsNullOrWhiteSpace(record.Sha256)
                || string.IsNullOrWhiteSpace(record.Reason)
                || record.Sha256.Length != 64 || !record.Sha256.All(Uri.IsHexDigit) || record.FileSize < 0)
            || records.GroupBy(record => record.Path, PathComparer).Any(group => group.Count() != 1))
            throw new IOException("The backup index contains invalid or duplicate records; it was not overwritten.");
        try
        {
            if (records.Any(record => !Path.IsPathFullyQualified(record.Path) || !Path.IsPathFullyQualified(record.OriginalPath)))
                throw new IOException("The backup index contains relative paths; it was not overwritten.");
            records = records.Select(record => record with
            {
                Path = Path.GetFullPath(record.Path),
                OriginalPath = Path.GetFullPath(record.OriginalPath)
            }).ToList();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new IOException("The backup index contains invalid paths; it was not overwritten.", exception);
        }
        if (records.GroupBy(record => record.Path, PathComparer).Any(group => group.Count() != 1))
            throw new IOException("The backup index contains equivalent duplicate paths; it was not overwritten.");
        return records;
    }

    private void SaveRecords(List<BackupRecord> records)
    {
        EnsureStoreDirectory();
        if (File.Exists(_indexPath)) EnsureDirectStoreFile(_indexPath);
        var temporaryPath = _indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteNewFlushedFile(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(records, _jsonOptions));
            if (File.Exists(_indexPath)) File.Replace(temporaryPath, _indexPath, null);
            else File.Move(temporaryPath, _indexPath);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private byte[] ReadVerifiedBytes(BackupRecord record)
    {
        EnsureDirectStoreFile(record.Path);
        if (new FileInfo(record.Path).Length != record.FileSize)
            throw new IOException("The backup file no longer matches its recorded size.");
        var bytes = ReadBoundedBytes(record.Path);
        if (bytes.LongLength != record.FileSize || !string.Equals(ComputeSha256(bytes), record.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The backup file no longer matches its recorded size or hash.");
        return bytes;
    }

    private static int FindRecord(List<BackupRecord> records, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var index = records.FindIndex(record => PathComparer.Equals(record.Path, fullPath));
        return index >= 0 ? index : throw new IOException("This backup is not in the index. Its ownership cannot be inferred from its filename.");
    }

    private static byte[] ReadBoundedBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > SaveCodec.MaximumFileBytes)
            throw new IOException("The file exceeds the supported 16 MiB save/backup limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void EnsureStoreDirectory()
    {
        RejectLinks(BackupDirectory);
        Directory.CreateDirectory(BackupDirectory);
        RejectLinks(BackupDirectory);
    }

    private void EnsureDirectStoreFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!PathComparer.Equals(Path.GetDirectoryName(fullPath), BackupDirectory))
            throw new IOException("The backup path is outside the exact backup directory.");
        RejectLinks(fullPath);
        if (Directory.Exists(fullPath))
            throw new IOException("A backup file path is a directory.");
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked or redirected backup paths are not eligible for this operation.");
        }
    }

    private static void WriteNewFlushedFile(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void ValidateSaveBytes(byte[] bytes, string message)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var save = _codec.Read(stream);
            if (!_validator.Validate(save).CanWrite)
                throw new UnsupportedSaveException(message);
        }
        catch (UnsupportedSaveException) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            throw new UnsupportedSaveException(message);
        }
    }

    private sealed class StoreLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
