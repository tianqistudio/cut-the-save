using System.IO;
using System.IO.Compression;
using CutTheSave.App;

internal static class SupportBundleChecks
{
    internal static void Run(string root)
    {
        var directory = Path.Combine(root, "support-export");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "support.zip");
        var service = new SupportBundleService();
        service.Create(output, 60, [], []);
        var original = File.ReadAllBytes(output);

        // Explorer and other readers can retain the old file while allowing replacement.
        using (var reader = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            service.Create(output, 144, [], []);
            using var oldBytes = new MemoryStream();
            reader.CopyTo(oldBytes);
            Check(original.SequenceEqual(oldBytes.ToArray()), "an open reader retains the previous complete bundle");
            using var archive = ZipFile.OpenRead(output);
            using var environment = new StreamReader(archive.GetEntry("environment.json")!.Open());
            Check(environment.ReadToEnd().Contains("144", StringComparison.Ordinal), "replacement publishes the complete new bundle");
            Check(archive.Entries.Count == 6, "all diagnostic entries are included");
        }

        var replacement = File.ReadAllBytes(output);
        using (var locked = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { service.Create(output, 240, [], []); }
            catch (IOException) { failed = true; }
            Check(failed, "a reader that disallows replacement blocks the export");
            Check(replacement.SequenceEqual(File.ReadAllBytes(output)), "failed replacement preserves the existing bundle");
        }
        Check(Directory.GetFiles(directory).Length == 1, "completed and failed exports leave no staging files");
        Console.WriteLine("PASS support bundle complete replacement, active readers and failure preservation");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
