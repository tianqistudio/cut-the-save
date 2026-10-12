using System.IO;
using System.Text;

namespace CutTheSave.App;

internal static class AppSettingsFile
{
    private static readonly object Gate = new();

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CutTheSave",
        "CutTheSave.ini");

    internal static string? ReadGeneralValue(string key, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return null;

            var inGeneralSection = false;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (IsSectionHeader(trimmed))
                {
                    inGeneralSection = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inGeneralSection || trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0 || !line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                return line[(separator + 1)..].Trim();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    internal static void SetGeneralValue(string key, string? value, string? path = null) =>
        SetGeneralValues(new Dictionary<string, string?> { [key] = value }, path);

    internal static void SetGeneralValues(IReadOnlyDictionary<string, string?> values, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        path ??= DefaultPath;

        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.IndexOfAny(['=', '\r', '\n', '[', ']']) >= 0)
                throw new ArgumentException("INI setting keys must be single-line names without separators.", nameof(values));
            if (pair.Value?.IndexOfAny(['\r', '\n']) >= 0)
                throw new ArgumentException("INI setting values must be single-line values.", nameof(values));
        }

        lock (Gate)
        {
            var lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : [];

            EnsureGeneralSection(lines);
            foreach (var pair in values)
                SetValueInGeneralSection(lines, pair.Key, pair.Value);

            Directory.CreateDirectory(Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The settings path has no parent directory."));
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureGeneralSection(List<string> lines)
    {
        if (FindGeneralSection(lines).Start >= 0)
            return;

        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            lines.Add(string.Empty);
        lines.Add("[General]");
    }

    private static void SetValueInGeneralSection(List<string> lines, string key, string? value)
    {
        var section = FindGeneralSection(lines);
        var matches = new List<int>();
        for (var index = section.Start + 1; index < section.End; index++)
        {
            var separator = lines[index].IndexOf('=');
            if (separator > 0 && lines[index][..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                matches.Add(index);
        }

        if (value is null)
        {
            for (var index = matches.Count - 1; index >= 0; index--)
                lines.RemoveAt(matches[index]);
            return;
        }

        if (matches.Count == 0)
        {
            lines.Insert(section.End, $"{key}={value}");
            return;
        }

        lines[matches[0]] = $"{key}={value}";
        for (var index = matches.Count - 1; index >= 1; index--)
            lines.RemoveAt(matches[index]);
    }

    private static (int Start, int End) FindGeneralSection(IReadOnlyList<string> lines)
    {
        var start = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (!IsSectionHeader(trimmed))
                continue;

            if (start >= 0)
                return (start, index);
            if (trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                start = index;
        }

        return (start, lines.Count);
    }

    private static bool IsSectionHeader(string value) =>
        value.Length >= 2 && value[0] == '[' && value[^1] == ']';
}
