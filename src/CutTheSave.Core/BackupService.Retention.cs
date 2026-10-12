namespace CutTheSave.Core;

public sealed record BackupRetentionPolicy(int MaxBackups = 50, long MaxBytes = 200L * 1024 * 1024);
public sealed record BackupRetentionCandidate(BackupRecord Record, DateTime LastWriteTimeUtc, DateTime CreationTimeUtc);
public sealed record BackupRetentionPlan(
    string BackupDirectory, BackupRetentionPolicy Policy, IReadOnlyList<string> ProtectedPaths,
    IReadOnlyList<BackupRetentionCandidate> Candidates, int ExistingCount, long ExistingBytes,
    bool LimitsRemainExceeded, IReadOnlyList<string> Issues);
public sealed record BackupRetentionResult(int DeletedCount, long DeletedBytes, IReadOnlyList<string> Issues);

public sealed partial class BackupService
{
    public BackupRetentionPlan PlanRetention(BackupRetentionPolicy? policy = null, IEnumerable<string>? protectedPaths = null)
    {
        using var lease = BeginTransaction();
        EnsureRetentionStable();
        EnsureSaveRecoveryStable();
        return PlanRetentionCore(LoadRecords(), policy ?? new BackupRetentionPolicy(),
            (protectedPaths ?? []).Select(Path.GetFullPath).ToArray());
    }

    public BackupRetentionResult ApplyRetention(BackupRetentionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!PathComparer.Equals(plan.BackupDirectory, BackupDirectory))
            throw new InvalidOperationException("The retention plan belongs to another backup directory.");
        using var lease = BeginTransaction();
        EnsureRetentionStable();
        EnsureSaveRecoveryStable();
        var records = LoadRecords();
        // Re-plan under the store lease. Newly locked/needed snapshots must not be deleted,
        // and new candidates must never expand the previously reviewed exact path set.
        var current = PlanRetentionCore(records, plan.Policy, plan.ProtectedPaths);
        var eligible = current.Candidates.ToDictionary(candidate => candidate.Record.Path, PathComparer);
        var issues = new List<string>(current.Issues);
        var deletedCount = 0;
        long deletedBytes = 0;
        foreach (var planned in plan.Candidates)
        {
            if (!eligible.TryGetValue(planned.Record.Path, out var candidate) || candidate != planned)
            {
                issues.Add("A planned snapshot changed or is now protected; it was retained.");
                continue;
            }
            try
            {
                ReadVerifiedBytes(candidate.Record);
                var info = new FileInfo(candidate.Record.Path);
                if (info.LastWriteTimeUtc != candidate.LastWriteTimeUtc || info.CreationTimeUtc != candidate.CreationTimeUtc)
                    throw new IOException("The snapshot changed after retention planning.");
                RemoveWithRetentionJournal(records, candidate);
                deletedCount++;
                deletedBytes += candidate.Record.FileSize;
            }
            catch (Exception exception) when (exception is not BackupMaintenancePendingException
                && exception is IOException or UnauthorizedAccessException)
            {
                issues.Add("An expired snapshot could not be removed and remains recorded for a later retry.");
            }
        }
        return new BackupRetentionResult(deletedCount, deletedBytes, issues);
    }

    public BackupRetentionResult EnforceRetention(IEnumerable<string>? protectedPaths = null) =>
        ApplyRetention(PlanRetention(protectedPaths: protectedPaths));

    private BackupRetentionPlan PlanRetentionCore(List<BackupRecord> records, BackupRetentionPolicy policy, IReadOnlyList<string> protectedPaths)
    {
        if (policy.MaxBackups < 1 || policy.MaxBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(policy), "Retention limits must be positive.");
        var protectedSet = new HashSet<string>(protectedPaths, PathComparer);
        var issues = new List<string>();
        var present = new List<BackupRecord>();
        var actualSizes = new Dictionary<string, long>(PathComparer);
        foreach (var record in records)
        {
            try
            {
                EnsureDirectStoreFile(record.Path);
                if (File.Exists(record.Path))
                {
                    actualSizes[record.Path] = new FileInfo(record.Path).Length;
                    present.Add(record);
                }
                else issues.Add("A recorded snapshot is missing; its history was retained.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add("An untrusted or linked backup path was retained without following it.");
            }
        }
        foreach (var latest in present.GroupBy(record => record.OriginalPath, PathComparer)
                     .Select(group => group.OrderByDescending(record => record.CreatedAt).ThenByDescending(record => record.Path, PathComparer).First()))
            protectedSet.Add(latest.Path);

        var count = present.Count;
        var bytes = actualSizes.Values.Sum();
        var candidates = new List<BackupRetentionCandidate>();
        foreach (var record in present.OrderBy(record => record.CreatedAt).ThenBy(record => record.Path, PathComparer))
        {
            if (count <= policy.MaxBackups && bytes <= policy.MaxBytes) break;
            if (record.IsLocked || protectedSet.Contains(record.Path) || !record.RetentionEligible || !HasManagedName(record.Path))
                continue;
            try
            {
                ReadVerifiedBytes(record);
                var info = new FileInfo(record.Path);
                candidates.Add(new BackupRetentionCandidate(record, info.LastWriteTimeUtc, info.CreationTimeUtc));
                count--;
                bytes -= record.FileSize;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add("A changed or unreadable snapshot was retained without automatic deletion.");
            }
        }
        return new BackupRetentionPlan(BackupDirectory, policy, protectedPaths.ToArray(), candidates,
            present.Count, actualSizes.Values.Sum(),
            count > policy.MaxBackups || bytes > policy.MaxBytes, issues);
    }

    private static bool HasManagedName(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("cts-v1-", StringComparison.Ordinal) && name.Length > 39
            && Guid.TryParseExact(name.Substring(7, 32), "N", out _) && name[39] == '_';
    }
}
