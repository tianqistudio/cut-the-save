namespace CutTheSave.Infrastructure.Windows;

public sealed record SaveLocation(string Path, bool Exists, bool IsOverride);

public sealed class SaveLocator
{
    public SaveLocation GetDefaultLocation(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return new SaveLocation(overridePath, File.Exists(overridePath), IsOverride: true);

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(
            documents,
            "SavedGames",
            "CutTheRope",
            "Preferences",
            "Player1",
            "ctr_save.bin");
        return new SaveLocation(path, File.Exists(path), IsOverride: false);
    }

    public IReadOnlyList<string> FindNearbyCandidates()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = Path.Combine(documents, "SavedGames", "CutTheRope");
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateFiles(root, "ctr_save.bin", SearchOption.AllDirectories)
            .Take(20)
            .ToArray();
    }
}
