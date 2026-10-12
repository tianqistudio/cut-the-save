using System.Security.Cryptography;
using System.Text;

namespace CutTheSave.Infrastructure.Windows;

public sealed record RtssUndoResult(bool RemovedCreatedProfile, RtssProfileState CurrentState);

public sealed class RtssRepairWorkflow
{
    private readonly RtssProfileService _profileService;
    private readonly RtssStateStore _stateStore;
    private readonly string _backupDirectory;
    internal Action<string>? Checkpoint { get; init; }

    public RtssRepairWorkflow() : this(new(), new(), RtssRepairStoragePaths.BackupDirectory) { }
    internal RtssRepairWorkflow(RtssProfileService profileService, RtssStateStore stateStore, string backupDirectory)
    {
        _profileService = profileService;
        _stateStore = stateStore;
        _backupDirectory = backupDirectory;
    }

    public RtssProfileState Apply60Fps(RtssInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        using var gate = new RepairGate(installation.InstallPath);
        if (_stateStore.StateFileExists)
            throw new InvalidOperationException("An RTSS repair or interrupted operation is already recorded. Review or undo it first.");
        PersistedRtssRepairState? intent = null;
        var applied = _profileService.Apply60Fps(installation, _backupDirectory, beforeWrite: before =>
        {
            var fingerprint = RtssProfileFingerprint.Capture(before.ProfilePath);
            if (fingerprint is not null)
            {
                var backup = RtssProfileFingerprint.Capture(before.BackupPath)
                    ?? throw new IOException("The original RTSS Profile backup is missing.");
                if (backup.Length != fingerprint.Length || backup.Sha256 != fingerprint.Sha256)
                    throw new IOException("The RTSS Profile changed before its write intent was recorded.");
            }
            intent = new(installation.InstallPath, before.ProfileName, before.HadOriginalProfile, before.BackupPath, DateTimeOffset.UtcNow)
                { SchemaVersion = 2, Phase = "prepared", Before = fingerprint };
            _stateStore.Save(intent);
            Checkpoint?.Invoke("after-intent");
            if (RtssProfileFingerprint.Capture(_profileService.Read(installation).ProfilePath) != fingerprint)
                throw new IOException("The RTSS Profile changed after preparation; no write is permitted.");
        });
        Checkpoint?.Invoke("after-profile");
        var after = RtssProfileFingerprint.Capture(applied.ProfilePath)
            ?? throw new IOException("The applied RTSS Profile could not be verified. Keep the repair record.");
        _stateStore.Save(intent! with { Phase = "applied", After = after });
        Checkpoint?.Invoke("after-commit");
        return applied;
    }

    public RtssUndoResult Undo(RtssInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        using var gate = new RepairGate(installation.InstallPath);
        var state = _stateStore.Load() ?? throw new InvalidDataException("RTSS repair record is missing or unreadable; no state will be guessed.");
        if (!string.Equals(Path.GetFullPath(state.InstallPath), Path.GetFullPath(installation.InstallPath), StringComparison.OrdinalIgnoreCase)
            || state.ProfileName != RtssProfileService.TargetProfile)
            throw new InvalidDataException("RTSS repair record does not match this game installation.");
        if (state.SchemaVersion != 2 || state.Phase is not ("prepared" or "applied" or "undoing")
            || state.HadOriginalProfile != (state.Before is not null))
            throw new InvalidDataException("Legacy or incomplete RTSS repair record. Preserve the backup and review it manually; automatic undo is disabled.");
        var current = _profileService.Read(installation);
        var fingerprint = RtssProfileFingerprint.Capture(current.ProfilePath);
        // A process exit before writing or after completing undo is safe to acknowledge.
        if (state.Phase is "prepared" or "undoing" && fingerprint == state.Before)
        {
            _stateStore.Clear();
            return new(!state.HadOriginalProfile, current);
        }
        if (state.After is null || fingerprint != state.After)
            throw new IOException("RTSS Profile differs from the verified repair. External changes or an interrupted write require review; all files and the record are retained.");
        if (state.Before is not null)
        {
            var backup = RtssProfileFingerprint.Capture(state.BackupPath);
            if (backup is null || backup.Sha256 != state.Before.Sha256 || backup.Length != state.Before.Length)
                throw new IOException("The original RTSS backup changed; automatic undo stopped.");
        }
        _stateStore.Save(state with { Phase = "undoing" });
        Checkpoint?.Invoke("before-undo");
        if (state.HadOriginalProfile)
            current = _profileService.Restore(installation, state.BackupPath!, state.ProfileName, state.After);
        else
        {
            _profileService.RemoveCreatedProfile(installation, state.ProfileName, state.After);
            current = _profileService.Read(installation);
        }
        Checkpoint?.Invoke("after-undo");
        if (RtssProfileFingerprint.Capture(current.ProfilePath) != state.Before)
            throw new IOException("RTSS undo read-back differs from the original; retain the recovery record.");
        _stateStore.Clear();
        return new(!state.HadOriginalProfile, current);
    }

    private sealed class RepairGate : IDisposable
    {
        private readonly Mutex _mutex;
        public RepairGate(string path)
        {
            var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
            _mutex = new(false, "Local\\CutTheSave.RtssRepair." + name);
            try { if (!_mutex.WaitOne(0)) throw new IOException("Another RTSS repair operation is active."); }
            catch (AbandonedMutexException) { }
            catch { _mutex.Dispose(); throw; }
        }
        public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
    }
}

