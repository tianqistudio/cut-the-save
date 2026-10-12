using System.Diagnostics;
using System.Security.Cryptography;

namespace CutTheSave.Infrastructure.Windows;

public enum RtssCompatibilityKind { Unavailable, UnknownVersion, UnverifiedFiles, ProfileApiVerified, KnownGood }

public sealed record RtssCompatibilityResult(RtssCompatibilityKind Kind, string? Version)
{
    public bool CanUseProfileApi => Kind is RtssCompatibilityKind.ProfileApiVerified or RtssCompatibilityKind.KnownGood;
}

public static class RtssCompatibility
{
    // See docs/rtss-safety.md. This exact signed pair has local Profile API
    // read-back evidence, NOT a completed game-speed/undo field acceptance.
    public const string ObservedVersion = "7.3.5.28314";
    private const string ObservedExeHash = "84e6e439d313dcee0ba9549d8248d3923d9de6805d0380be5eab337446856736";
    private const string ObservedHooksHash = "68c496dea7ded5b8766092f0159f4e9ba3947df0829f594eb6981d2b06352ad5";

    public static RtssCompatibilityResult AssessEvidence(string? version, string? executableSha256, string? hooksSha256)
    {
        if (!System.Version.TryParse(version, out var parsed) || parsed.ToString() != ObservedVersion)
            return new(RtssCompatibilityKind.UnknownVersion, version);
        return new(string.Equals(executableSha256, ObservedExeHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(hooksSha256, ObservedHooksHash, StringComparison.OrdinalIgnoreCase)
                ? RtssCompatibilityKind.ProfileApiVerified : RtssCompatibilityKind.UnverifiedFiles, version);
    }

    public static RtssCompatibilityResult Inspect(string installPath)
    {
        try { using var lease = RtssBinaryLease.Open(installPath); return lease.Result; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { return new(RtssCompatibilityKind.Unavailable, null); }
    }

    public static IDisposable LockVerifiedBinaries(string installPath)
    {
        var lease = RtssBinaryLease.Open(installPath);
        if (lease.Result.CanUseProfileApi) return lease;
        lease.Dispose();
        throw new InvalidOperationException("The RTSS binary pair is not verified.");
    }

    internal sealed class RtssBinaryLease : IDisposable
    {
        private readonly FileStream _exe;
        private readonly FileStream _hooks;
        public RtssCompatibilityResult Result { get; }
        private RtssBinaryLease(FileStream exe, FileStream hooks, string version)
        {
            _exe = exe; _hooks = hooks;
            Result = AssessEvidence(version, Convert.ToHexString(SHA256.HashData(exe)), Convert.ToHexString(SHA256.HashData(hooks)));
        }

        public static RtssBinaryLease Open(string installPath)
        {
            if (!Path.IsPathFullyQualified(installPath) || installPath.StartsWith(@"\\", StringComparison.Ordinal))
                throw new IOException("RTSS must use a local absolute installation path.");
            EnsureNoLinks(installPath);
            var exePath = Path.Combine(installPath, "RTSS.exe");
            var hooksPath = Path.Combine(installPath, "RTSSHooks64.dll");
            EnsureNoLinks(exePath); EnsureNoLinks(hooksPath);
            var exe = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            FileStream? hooks = null;
            try
            {
                hooks = new FileStream(hooksPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (exe.Length is <= 0 or > 32 * 1024 * 1024 || hooks.Length is <= 0 or > 32 * 1024 * 1024)
                    throw new IOException("Unexpected RTSS binary size.");
                return new(exe, hooks, FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "");
            }
            catch { hooks?.Dispose(); exe.Dispose(); throw; }
        }

        private static void EnsureNoLinks(string path)
        {
            for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("RTSS installation cannot cross a link or reparse point.");
        }
        public void Dispose() { _hooks.Dispose(); _exe.Dispose(); }
    }
}
