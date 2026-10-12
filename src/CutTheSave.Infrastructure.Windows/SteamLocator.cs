using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CutTheSave.Infrastructure.Windows;

public sealed record GameInstallation(string LibraryPath, string GameDirectory, string ExecutablePath);

public sealed class SteamLocator
{
    private const int AppId = 223280;

    public IReadOnlyList<GameInstallation> FindCutTheRopeInstallations()
    {
        return FindCutTheRopeInstallations(FindSteamLibraries());
    }

    internal static IReadOnlyList<GameInstallation> FindCutTheRopeInstallations(IEnumerable<string> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var results = new List<GameInstallation>();

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
            var installDirectoryName = File.Exists(manifestPath) ? ParseInstallDirectoryName(manifestPath) : null;
            var gameDirectory = Path.Combine(library, "steamapps", "common", installDirectoryName ?? "Cut the Rope");
            var executablePath = Path.Combine(gameDirectory, "CutTheRopeApp.exe");

            if (File.Exists(executablePath) || File.Exists(manifestPath))
            {
                results.Add(new GameInstallation(library, gameDirectory, executablePath));
            }
        }

        return results
            .OrderByDescending(item => File.Exists(item.ExecutablePath))
            .ThenByDescending(item => Directory.Exists(item.GameDirectory)
                ? Directory.GetLastWriteTimeUtc(item.GameDirectory)
                : DateTime.MinValue)
            .ToArray();
    }

    private static IEnumerable<string> FindSteamLibraries()
    {
        var roots = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    var installPath = key?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(installPath))
                        roots.Add(installPath);
                }
                catch (SecurityException)
                {
                    // A registry view may be unavailable; continue with the other views.
                }
            }
        }

        foreach (var root in roots)
        {
            yield return root;
            var vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                continue;

            foreach (var path in ParseLibraryPaths(vdfPath))
                yield return path;
        }
    }

    private static IEnumerable<string> ParseLibraryPaths(string path)
    {
        var pattern = new Regex(
            "^\\s*\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var match = pattern.Match(line);
            if (match.Success)
                yield return match.Groups["path"].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
        }
    }

    private static string? ParseInstallDirectoryName(string manifestPath)
    {
        try
        {
            var text = File.ReadAllText(manifestPath);
            return ParseInstallDirectoryNameFromText(text);
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

    internal static string? ParseInstallDirectoryNameFromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = Regex.Match(text, "\\\"installdir\\\"\\s+\\\"(?<dir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var value = match.Groups["dir"].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(value) || value is "." or ".." || Path.GetFileName(value) != value ? null : value;
    }
}
