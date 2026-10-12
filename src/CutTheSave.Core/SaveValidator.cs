namespace CutTheSave.Core;

public enum SaveSeverity
{
    Warning,
    Error
}

public sealed record SaveValidationIssue(SaveSeverity Severity, string Code, string Message);

public sealed class SaveValidationResult
{
    public IReadOnlyList<SaveValidationIssue> Issues { get; }

    public bool IsValid => Issues.All(issue => issue.Severity != SaveSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == SaveSeverity.Warning);

    public bool CanWrite => IsValid && !HasWarnings;

    public SaveValidationResult(IEnumerable<SaveValidationIssue> issues)
    {
        Issues = issues.ToArray();
    }
}

public sealed class SaveValidator
{
    public SaveValidationResult Validate(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var issues = new List<SaveValidationIssue>();

        AddDuplicateIssues(save.IntEntries.Select(entry => entry.Key), "INT-DUPLICATE", issues);
        AddDuplicateIssues(save.StringEntries.Select(entry => entry.Key), "STRING-DUPLICATE", issues);

        if (!save.IntEntries.Any(entry => entry.Key.StartsWith("SCORE_", StringComparison.Ordinal)
                || entry.Key.StartsWith("STARS_", StringComparison.Ordinal)
                || entry.Key.StartsWith("UNLOCKED_", StringComparison.Ordinal)
                || entry.Key is "PREFS_LEVELS_WON" or "PREFS_LAST_PACK"))
            issues.Add(new SaveValidationIssue(SaveSeverity.Warning, "FORMAT-UNRECOGNIZED",
                "No supported progress field was found; the container is view-only."));

        foreach (var entry in save.IntEntries)
        {
            if (entry.Key.StartsWith("SCORE_", StringComparison.Ordinal) &&
                (entry.Value < 0 || entry.Value > 5_999))
            {
                issues.Add(new SaveValidationIssue(
                    SaveSeverity.Error,
                    "SCORE-RANGE",
                    $"Score field '{entry.Key}' has value {entry.Value}; expected 0..5999."));
            }

            if (entry.Key.StartsWith("STARS_", StringComparison.Ordinal) &&
                (entry.Value < 0 || entry.Value > 3))
            {
                issues.Add(new SaveValidationIssue(
                    SaveSeverity.Error,
                    "STARS-RANGE",
                    $"Stars field '{entry.Key}' has value {entry.Value}; expected 0..3."));
            }

            if (entry.Key.StartsWith("UNLOCKED_", StringComparison.Ordinal) &&
                entry.Value is not 0 and not 1)
            {
                issues.Add(new SaveValidationIssue(
                    SaveSeverity.Error,
                    "UNLOCKED-RANGE",
                    $"Unlocked field '{entry.Key}' has value {entry.Value}; expected 0 or 1."));
            }
        }

        if (save.TryGetInt("PREFS_LEVELS_WON", out var levelsWon) && (levelsWon < 0 || levelsWon > 275))
        {
            issues.Add(new SaveValidationIssue(
                SaveSeverity.Error,
                "LEVELS-WON-RANGE",
                $"PREFS_LEVELS_WON has value {levelsWon}; expected 0..275."));
        }

        if (save.TryGetInt("PREFS_LAST_PACK", out var lastPack) && (lastPack < 0 || lastPack > 10))
        {
            issues.Add(new SaveValidationIssue(
                SaveSeverity.Error,
                "LAST-PACK-RANGE",
                $"PREFS_LAST_PACK has value {lastPack}; expected 0..10."));
        }

        if (save.TryGetString("PREFS_SCORE_HASH", out var storedHash) && !storedHash.EndsWith('\0'))
        {
            issues.Add(new SaveValidationIssue(
                SaveSeverity.Warning,
                "HASH-TERMINATOR",
                "PREFS_SCORE_HASH does not end with the expected NUL terminator."));
        }

        return new SaveValidationResult(issues);
    }

    private static void AddDuplicateIssues(
        IEnumerable<string> keys,
        string code,
        ICollection<SaveValidationIssue> issues)
    {
        foreach (var key in keys.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(new SaveValidationIssue(
                SaveSeverity.Warning,
                code,
                $"The key '{key.Key}' appears {key.Count()} times; unknown duplicate semantics require read-only caution."));
        }
    }
}
