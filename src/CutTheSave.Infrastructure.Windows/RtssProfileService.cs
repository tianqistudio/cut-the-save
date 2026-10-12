using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CutTheSave.Infrastructure.Windows;

public sealed record RtssProfileState(
    string ProfileName,
    int? FramerateLimit,
    int? FramerateLimitDenominator,
    string? ProfilePath,
    bool HadOriginalProfile,
    string? BackupPath);

internal interface IRtssProfileApi : IDisposable
{
    void LoadProfile(string profileName);
    int? GetInt(string propertyName);
    void SetInt(string propertyName, int value);
    void SaveProfile(string profileName);
    void DeleteProfile(string profileName);
    void UpdateProfiles();
}

public sealed class RtssProfileService
{
    public const string TargetProfile = "CutTheRopeApp.exe";
    public const int TargetFps = 60;
    private readonly Func<string, IRtssProfileApi> _apiFactory;
    private readonly bool _requireVerifiedBinaries;

    public RtssProfileService()
        : this(RtssNativeApi.Load)
    {
        _requireVerifiedBinaries = true;
    }

    internal RtssProfileService(Func<string, IRtssProfileApi> apiFactory)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
    }

    public RtssProfileState Read(RtssInstallation installation, string profileName = TargetProfile)
    {
        ValidateInstallation(installation, profileName);
        using var api = _apiFactory(installation.HooksPath!);
        api.LoadProfile(profileName);
        var profilePath = FindProfilePath(installation.InstallPath, profileName);
        return new RtssProfileState(
            profileName,
            api.GetInt("FramerateLimit"),
            api.GetInt("FramerateLimitDenominator"),
            profilePath,
            profilePath is not null,
            BackupPath: null);
    }

    public RtssProfileState Apply60Fps(
        RtssInstallation installation,
        string backupDirectory,
        string profileName = TargetProfile,
        Action<RtssProfileState>? beforeWrite = null)
    {
        ValidateInstallation(installation, profileName);
        if (GameProcessGuard.IsRunning())
            throw new InvalidOperationException("请先关闭 CutTheRopeApp.exe，再修改 RTSS Profile。");

        Directory.CreateDirectory(backupDirectory);
        var profilePath = FindProfilePath(installation.InstallPath, profileName);
        var backupPath = BackupExistingProfile(profilePath, backupDirectory, profileName);

        beforeWrite?.Invoke(new(profileName, null, null, profilePath, profilePath is not null, backupPath));

        try
        {
            using var api = _apiFactory(installation.HooksPath!);
            api.LoadProfile(profileName);
            api.SetInt("FramerateLimitDenominator", 1);
            api.SetInt("FramerateLimit", TargetFps);
            api.SaveProfile(profileName);
            api.UpdateProfiles();

            var readBack = Read(installation, profileName);
            if (readBack.FramerateLimit != TargetFps || readBack.FramerateLimitDenominator != 1)
                throw new IOException("RTSS Profile 回读值不是 60/1，已拒绝报告为成功。");

            return readBack with { BackupPath = backupPath, HadOriginalProfile = profilePath is not null };
        }
        catch
        {
            // A journaled caller retains uncertain bytes and its intent for explicit recovery.
            if (beforeWrite is not null) throw;
            if (backupPath is not null && profilePath is not null && File.Exists(backupPath))
            {
                File.Copy(backupPath, profilePath, overwrite: true);
                TryRefreshProfiles(installation, profileName);
            }
            else if (profilePath is null)
            {
                // A failed first-time write must not leave a half-created Profile behind.
                TryRemoveCreatedProfile(installation, profileName);
            }
            throw;
        }
    }

    public RtssProfileState Restore(
        RtssInstallation installation,
        string backupPath,
        string profileName = TargetProfile,
        RtssProfileFingerprint? expectedCurrent = null)
    {
        ValidateInstallation(installation, profileName);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("找不到 RTSS Profile 备份。", backupPath);
        if (GameProcessGuard.IsRunning())
            throw new InvalidOperationException("请先关闭 CutTheRopeApp.exe，再恢复 RTSS Profile。");

        var expectedHash = SHA256.HashData(File.ReadAllBytes(backupPath));
        var profilePath = FindProfilePath(installation.InstallPath, profileName)
            ?? Path.Combine(installation.InstallPath, "Profiles", profileName + ".cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        if (expectedCurrent is not null && RtssProfileFingerprint.Capture(profilePath) != expectedCurrent)
            throw new IOException("RTSS Profile changed after the repair; automatic undo stopped.");
        File.Copy(backupPath, profilePath, overwrite: true);
        var restoredHash = SHA256.HashData(File.ReadAllBytes(profilePath));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, restoredHash))
            throw new IOException("RTSS Profile 恢复后与备份不一致，已拒绝报告为成功。");

        using var api = _apiFactory(installation.HooksPath!);
        api.LoadProfile(profileName);
        api.UpdateProfiles();
        return Read(installation, profileName) with { BackupPath = backupPath };
    }

    public void RemoveCreatedProfile(
        RtssInstallation installation,
        string profileName = TargetProfile,
        RtssProfileFingerprint? expectedCurrent = null)
    {
        ValidateInstallation(installation, profileName);
        if (GameProcessGuard.IsRunning())
            throw new InvalidOperationException("请先关闭 CutTheRopeApp.exe，再删除 RTSS Profile。");

        if (expectedCurrent is not null && RtssProfileFingerprint.Capture(FindProfilePath(installation.InstallPath, profileName)) != expectedCurrent)
            throw new IOException("RTSS Profile changed after the repair; automatic removal stopped.");
        using var api = _apiFactory(installation.HooksPath!);
        api.DeleteProfile(profileName);
        api.UpdateProfiles();
        if (FindProfilePath(installation.InstallPath, profileName) is not null)
            throw new IOException("RTSS Profile 删除后仍然存在，已拒绝报告为成功。");
    }

    private void ValidateInstallation(RtssInstallation installation, string profileName)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!string.Equals(profileName, TargetProfile, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the CutTheRopeApp.exe Profile is permitted.");
        if (!installation.HasHooks || string.IsNullOrWhiteSpace(installation.HooksPath))
            throw new InvalidOperationException("RTSSHooks64.dll 不存在，不能读写 RTSS Profile。");
        if (_requireVerifiedBinaries && (!Path.IsPathFullyQualified(installation.InstallPath) ||
            !string.Equals(Path.GetFullPath(installation.HooksPath), Path.Combine(Path.GetFullPath(installation.InstallPath), "RTSSHooks64.dll"), StringComparison.OrdinalIgnoreCase) ||
            !RtssCompatibility.Inspect(installation.InstallPath).CanUseProfileApi))
            throw new InvalidOperationException("This RTSS installation has not passed the binary compatibility check.");
    }

    private static string? FindProfilePath(string installPath, string profileName)
    {
        var profiles = Path.Combine(installPath, "Profiles");
        var candidates = new[]
        {
            Path.Combine(profiles, profileName + ".cfg"),
            Path.Combine(profiles, profileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? BackupExistingProfile(string? profilePath, string backupDirectory, string profileName)
    {
        if (profilePath is null || !File.Exists(profilePath))
            return null;

        var safeName = profileName.Replace("\\", "_", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal);
        var backupPath = Path.Combine(
            backupDirectory,
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{safeName}.profile.bak");
        File.Copy(profilePath, backupPath, overwrite: false);
        using (var durable = new FileStream(backupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) durable.Flush(true);
        var original = RtssProfileFingerprint.Capture(profilePath)!;
        var backup = RtssProfileFingerprint.Capture(backupPath)!;
        if (original.Length != backup.Length || original.Sha256 != backup.Sha256)
            throw new IOException("RTSS Profile changed while it was being backed up.");
        return backupPath;
    }

    private void TryRefreshProfiles(RtssInstallation installation, string profileName)
    {
        try
        {
            using var api = _apiFactory(installation.HooksPath!);
            api.LoadProfile(profileName);
            api.UpdateProfiles();
        }
        catch
        {
            // Preserve the original write exception; the next read will expose any refresh issue.
        }
    }

    private void TryRemoveCreatedProfile(RtssInstallation installation, string profileName)
    {
        try
        {
            using var api = _apiFactory(installation.HooksPath!);
            api.DeleteProfile(profileName);
            api.UpdateProfiles();
        }
        catch
        {
            // Preserve the original write exception; do not mask it with best-effort cleanup.
        }
    }

    private sealed class RtssNativeApi : IRtssProfileApi
    {
        private readonly nint _handle;
        private readonly RtssCompatibility.RtssBinaryLease _binaryLease;
        private readonly LoadProfileDelegate _loadProfile;
        private readonly GetProfilePropertyDelegate _getProfileProperty;
        private readonly SetProfilePropertyDelegate _setProfileProperty;
        private readonly SaveProfileDelegate _saveProfile;
        private readonly DeleteProfileDelegate _deleteProfile;
        private readonly UpdateProfilesDelegate _updateProfiles;

        private RtssNativeApi(nint handle, RtssCompatibility.RtssBinaryLease binaryLease)
        {
            _handle = handle;
            _binaryLease = binaryLease;
            _loadProfile = GetDelegate<LoadProfileDelegate>(handle, "LoadProfile");
            _getProfileProperty = GetDelegate<GetProfilePropertyDelegate>(handle, "GetProfileProperty");
            _setProfileProperty = GetDelegate<SetProfilePropertyDelegate>(handle, "SetProfileProperty");
            _saveProfile = GetDelegate<SaveProfileDelegate>(handle, "SaveProfile");
            _deleteProfile = GetDelegate<DeleteProfileDelegate>(handle, "DeleteProfile");
            _updateProfiles = GetDelegate<UpdateProfilesDelegate>(handle, "UpdateProfiles");
        }

        public static RtssNativeApi Load(string absolutePath)
        {
            if (!Path.IsPathFullyQualified(absolutePath) || !File.Exists(absolutePath) ||
                !Path.GetFileName(absolutePath).Equals("RTSSHooks64.dll", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("RTSSHooks64.dll 路径无效。", absolutePath);
            var lease = RtssCompatibility.RtssBinaryLease.Open(Path.GetDirectoryName(absolutePath)!);
            nint handle = 0;
            try
            {
                if (!lease.Result.CanUseProfileApi) throw new InvalidOperationException("This RTSS binary pair has not been verified for Profile API access.");
                handle = NativeLibrary.Load(absolutePath, typeof(RtssProfileService).Assembly, DllImportSearchPath.System32);
                return new RtssNativeApi(handle, lease);
            }
            catch
            {
                if (handle != 0) NativeLibrary.Free(handle);
                lease.Dispose();
                throw;
            }
        }

        public void LoadProfile(string profileName) => _loadProfile(profileName);

        public int? GetInt(string propertyName)
        {
            var buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buffer, 0);
                return _getProfileProperty(propertyName, buffer, sizeof(int))
                    ? Marshal.ReadInt32(buffer)
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void SetInt(string propertyName, int value)
        {
            var buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buffer, value);
                if (!_setProfileProperty(propertyName, buffer, sizeof(int)))
                    throw new IOException($"RTSS 未接受 Profile 属性：{propertyName}。");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void SaveProfile(string profileName) => _saveProfile(profileName);
        public void DeleteProfile(string profileName) => _deleteProfile(profileName);
        public void UpdateProfiles() => _updateProfiles();

        public void Dispose() { NativeLibrary.Free(_handle); _binaryLease.Dispose(); }

        private static T GetDelegate<T>(nint handle, string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate void LoadProfileDelegate([MarshalAs(UnmanagedType.LPStr)] string profile);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate bool GetProfilePropertyDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            nint data,
            uint size);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate bool SetProfilePropertyDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            nint data,
            uint size);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate void SaveProfileDelegate([MarshalAs(UnmanagedType.LPStr)] string profile);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate void DeleteProfileDelegate([MarshalAs(UnmanagedType.LPStr)] string profile);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UpdateProfilesDelegate();
    }
}
