using CutTheSave.Infrastructure.Windows;

internal static class SteamLocatorTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CutTheSave-steam-locator-test-{Guid.NewGuid():N}");
        try
        {
            var older = CreateInstallation(root, "older", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), hasExecutable: true);
            var newer = CreateInstallation(root, "newer", new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), hasExecutable: true);
            var incomplete = CreateInstallation(root, "incomplete", new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), hasExecutable: false);

            // Input order deliberately conflicts with update order, and the incomplete
            // installation is newest so that both ranking rules must be respected.
            var results = SteamLocator.FindCutTheRopeInstallations([older, incomplete, newer, older]);
            Assert(results.Count == 3, "duplicate Steam libraries are deduplicated");
            Assert(results[0].LibraryPath == newer, "the newest runnable Steam installation is preferred");
            Assert(results[1].LibraryPath == older, "older runnable installations precede incomplete installations");
            Assert(results[2].LibraryPath == incomplete, "a recent manifest alone does not outrank a runnable installation");
            Assert(results.All(item => Path.GetFileName(item.GameDirectory) == "Custom Cut the Rope"),
                "Steam discovery honors the manifest installdir in each isolated library");

            Console.WriteLine("PASS isolated Steam discovery orders runnable installations by directory update time and deduplicates libraries");
        }
        finally
        {
            if (Directory.Exists(root) && Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1")
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateInstallation(string root, string name, DateTime updatedAtUtc, bool hasExecutable)
    {
        var library = Path.Combine(root, name);
        var steamapps = Path.Combine(library, "steamapps");
        var game = Path.Combine(steamapps, "common", "Custom Cut the Rope");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_223280.acf"), "\"installdir\"\t\"Custom Cut the Rope\"");
        if (hasExecutable)
            File.WriteAllText(Path.Combine(game, "CutTheRopeApp.exe"), "synthetic discovery fixture; not executable");
        Directory.SetLastWriteTimeUtc(game, updatedAtUtc);
        return library;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
