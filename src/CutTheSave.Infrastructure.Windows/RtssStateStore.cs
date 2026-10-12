using System.Text.Json;

namespace CutTheSave.Infrastructure.Windows;

public sealed record PersistedRtssRepairState(
    string InstallPath,
    string ProfileName,
    bool HadOriginalProfile,
    string? BackupPath,
    DateTimeOffset AppliedAtUtc)
{
    public int SchemaVersion { get; init; } = 1;
    public string Phase { get; init; } = "legacy";
    public RtssProfileFingerprint? Before { get; init; }
    public RtssProfileFingerprint? After { get; init; }
}

public sealed record RtssProfileFingerprint(string Path, long Length, string Sha256)
{
    public static RtssProfileFingerprint? Capture(string? path)
    {
        if (path is null) return null;
        var full = System.IO.Path.GetFullPath(path);
        for (var current = full; current is not null; current = System.IO.Path.GetDirectoryName(current))
            if (System.IO.Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("RTSS recovery refuses linked paths.");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new IOException("RTSS Profile exceeds the recovery size limit.");
        return new(full, stream.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)));
    }
}

public static class RtssRepairStoragePaths
{
    public static string MachineRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CutTheSave");

    public static string StatePath => Path.Combine(MachineRoot, "rtss-state.json");

    public static string BackupDirectory => Path.Combine(MachineRoot, "rtss-backups");

    public static string LegacyUserStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CutTheSave",
        "rtss-state.json");

    public static string LegacyUserBackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CutTheSave",
        "rtss-backups");
}

public sealed class RtssStateStore
{
    private readonly string _statePath;
    private readonly string? _fallbackStatePath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public RtssStateStore(string? statePath = null, string? fallbackStatePath = null)
    {
        if (statePath is null)
        {
            _statePath = RtssRepairStoragePaths.StatePath;
            _fallbackStatePath = fallbackStatePath ?? RtssRepairStoragePaths.LegacyUserStatePath;
        }
        else
        {
            _statePath = statePath;
            _fallbackStatePath = fallbackStatePath;
        }
    }

    public PersistedRtssRepairState? Load()
    {
        var primaryExists = File.Exists(_statePath);
        if (primaryExists)
            return TryLoad(_statePath);

        return !string.IsNullOrWhiteSpace(_fallbackStatePath) && File.Exists(_fallbackStatePath)
            ? TryLoad(_fallbackStatePath)
            : null;
    }

    private PersistedRtssRepairState? TryLoad(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 65536) return null;
            return JsonSerializer.Deserialize<PersistedRtssRepairState>(
                File.ReadAllText(path),
                _options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(PersistedRtssRepairState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(_statePath)
            ?? throw new InvalidOperationException("RTSS state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _statePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, _options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep the original state-write result; a stale temp file is never treated as repair state.
            }
        }
    }

    public void Clear()
    {
        if (File.Exists(_statePath))
            File.Delete(_statePath);
        if (!string.IsNullOrWhiteSpace(_fallbackStatePath) && File.Exists(_fallbackStatePath))
            File.Delete(_fallbackStatePath);
    }

    public string StatePath => _statePath;

    public string? FallbackStatePath => _fallbackStatePath;

    public bool StateFileExists =>
        File.Exists(_statePath) ||
        (!string.IsNullOrWhiteSpace(_fallbackStatePath) && File.Exists(_fallbackStatePath));
}
