using System.Security;
using System.Diagnostics;
using Microsoft.Win32;

namespace CutTheSave.Infrastructure.Windows;

public sealed record RtssInstallation(
    string InstallPath,
    string? Version,
    bool HasExecutable,
    bool HasHooks,
    bool IsRunning,
    string? ExecutablePath,
    string? HooksPath)
{
    public RtssCompatibilityResult Compatibility { get; init; } = new(RtssCompatibilityKind.Unavailable, null);
}

public sealed class RtssDetector
{
    public IReadOnlyList<RtssInstallation> FindInstallations()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in FindUninstallLocations())
            candidates.Add(path);

        foreach (var path in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RivaTuner Statistics Server"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server")
        })
        {
            if (!string.IsNullOrWhiteSpace(path))
                candidates.Add(path);
        }

        return candidates
            .Where(Directory.Exists)
            .Select(CreateInstallation)
            .Where(item => item.HasExecutable || item.HasHooks)
            .OrderByDescending(item => item.HasExecutable && item.HasHooks)
            .ThenByDescending(item => item.Compatibility.CanUseProfileApi)
            .ThenByDescending(item => Version.TryParse(item.Version, out var version) ? version : new Version())
            .ToArray();
    }

    private static RtssInstallation CreateInstallation(string installPath)
    {
        var executablePath = Path.Combine(installPath, "RTSS.exe");
        var hooksPath = Path.Combine(installPath, "RTSSHooks64.dll");
        var version = File.Exists(executablePath)
            ? FileVersionInfo.GetVersionInfo(executablePath).FileVersion
            : null;
        var processes = Process.GetProcessesByName("RTSS");
        var isRunning = processes.Length > 0;
        foreach (var process in processes) process.Dispose();

        return new RtssInstallation(
            installPath,
            version,
            File.Exists(executablePath),
            File.Exists(hooksPath),
            isRunning,
            File.Exists(executablePath) ? executablePath : null,
            File.Exists(hooksPath) ? hooksPath : null)
        { Compatibility = RtssCompatibility.Inspect(installPath) };
    }

    private static IEnumerable<string> FindUninstallLocations()
    {
        var results = new List<string>();
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        var hives = new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (var hive in hives)
        foreach (var view in views)
        foreach (var uninstallPath in paths)
        {
            RegistryKey? baseKey = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(uninstallPath);
                if (uninstall is null)
                    continue;

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(subKeyName);
                    var displayName = app?.GetValue("DisplayName") as string;
                    var installLocation = app?.GetValue("InstallLocation") as string;
                    if (displayName?.Contains("RivaTuner Statistics Server", StringComparison.OrdinalIgnoreCase) == true &&
                        !string.IsNullOrWhiteSpace(installLocation))
                    {
                        results.Add(installLocation);
                    }
                }
            }
            catch (SecurityException)
            {
                // Continue with other hives and registry views.
            }
            finally
            {
                baseKey?.Dispose();
            }
        }

        return results;
    }
}
