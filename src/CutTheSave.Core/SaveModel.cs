namespace CutTheSave.Core;

public sealed record SaveIntEntry(string Key, int Value);

public sealed record SaveStringEntry(string Key, string Value);

public sealed class CutTheRopeSave
{
    public List<SaveIntEntry> IntEntries { get; } = [];

    public List<SaveStringEntry> StringEntries { get; } = [];

    public IEnumerable<SaveIntEntry> FindIntEntries(string key) =>
        IntEntries.Where(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

    public IEnumerable<SaveStringEntry> FindStringEntries(string key) =>
        StringEntries.Where(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

    public bool TryGetInt(string key, out int value)
    {
        var entry = IntEntries.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));

        if (entry is null)
        {
            value = default;
            return false;
        }

        value = entry.Value;
        return true;
    }

    public bool TryGetString(string key, out string value)
    {
        var entry = StringEntries.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));

        if (entry is null)
        {
            value = string.Empty;
            return false;
        }

        value = entry.Value;
        return true;
    }

    public void SetInt(string key, int value)
    {
        var index = IntEntries.FindIndex(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));

        if (index >= 0)
        {
            IntEntries[index] = new SaveIntEntry(key, value);
            return;
        }

        IntEntries.Add(new SaveIntEntry(key, value));
    }

    public void SetString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var index = StringEntries.FindIndex(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));

        if (index >= 0)
        {
            StringEntries[index] = new SaveStringEntry(key, value);
            return;
        }

        StringEntries.Add(new SaveStringEntry(key, value));
    }
}
