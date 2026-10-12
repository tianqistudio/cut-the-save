using System.Globalization;
using System.IO;

namespace CutTheSave.App;

public sealed class UpdateCheckStateStore
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly string _path;

    public UpdateCheckStateStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CutTheSave",
            "update-check.ini"))
    {
    }

    public UpdateCheckStateStore(string path) => _path = path;

    public bool ShouldCheck(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(_path))
                return true;

            var value = File.ReadAllText(_path).Trim();
            return !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastSuccess)
                || now - lastSuccess >= CheckInterval;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void MarkSuccessful(DateTimeOffset now)
    {
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        File.Move(temporary, _path, overwrite: true);
    }
}
