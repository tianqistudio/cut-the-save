using System.IO.Compression;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CutTheSave.Infrastructure.Windows;

namespace CutTheSave.App;

public sealed class SupportBundleService
{
    public string Create(string outputPath, int? refreshRate, IReadOnlyList<GameInstallation> steamInstallations, IReadOnlyList<RtssInstallation> rtssInstallations)
    {
        var userName = Environment.UserName;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteText(archive, "app/version.txt", $"Cut the Save{Environment.NewLine}Version={version}{Environment.NewLine}Runtime={Environment.Version}{Environment.NewLine}");
                    WriteJson(archive, "environment.json", new
                    {
                        os = Environment.OSVersion.VersionString,
                        runtime = Environment.Version.ToString(),
                        architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                        processArchitecture = Environment.Is64BitProcess ? "x64" : "x86",
                        uiLanguage = System.Globalization.CultureInfo.CurrentUICulture.Name,
                        primaryRefreshRate = refreshRate,
                        saveIncluded = false
                    });
                    WriteJson(archive, "sanitized-settings.json", new
                    {
                        language = LocalizationService.ToCode(LocalizationService.Current),
                        updateConfigured = UpdateConfiguration.Load().IsReady,
                        telemetry = false,
                        saveContentIncluded = false
                    });
                    WriteJson(archive, "steam-summary.json", steamInstallations.Select(item => new
                    {
                        libraryPath = Sanitize(item.LibraryPath, userName),
                        gameDirectory = Sanitize(item.GameDirectory, userName),
                        executableFound = File.Exists(item.ExecutablePath)
                    }).ToArray());
                    WriteJson(archive, "rtss-summary.json", rtssInstallations.Select(item => new
                    {
                        installPath = Sanitize(item.InstallPath, userName),
                        version = item.Version,
                        hasExecutable = item.HasExecutable,
                        hasHooks = item.HasHooks,
                        isRunning = item.IsRunning
                    }).ToArray());
                    WriteText(archive, "logs/README.txt", "No save data is included. This build does not collect or upload telemetry.\n");
                }
                stream.Flush(flushToDisk: true);
            }
            // Publish only after the ZIP central directory is complete and the file is closed.
            if (File.Exists(destination))
                File.Replace(staging, destination, null);
            else
                File.Move(staging, destination);
            return outputPath;
        }
        finally
        {
            try
            {
                if (File.Exists(staging))
                    File.Delete(staging);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void WriteJson<T>(ZipArchive archive, string name, T value)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteText(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    private static string Sanitize(string value, string userName) => string.IsNullOrWhiteSpace(value) ? value : value.Replace(userName, "<USER>", StringComparison.OrdinalIgnoreCase);
}
