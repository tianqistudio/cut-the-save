namespace CutTheSave.Core;

public sealed class LevelProgress
{
    public LevelProgress(int boxIndex, int levelIndex)
    {
        BoxIndex = boxIndex;
        LevelIndex = levelIndex;
    }

    public int BoxIndex { get; }
    public int LevelIndex { get; }
    public string ScoreKey => $"SCORE_{BoxIndex}_{LevelIndex}";
    public string StarsKey => $"STARS_{BoxIndex}_{LevelIndex}";
    public string UnlockedKey => $"UNLOCKED_{BoxIndex}_{LevelIndex}";
    public int Score { get; internal set; }
    public int Stars { get; internal set; }
    public int Unlocked { get; internal set; }
    public bool IsComplete => Unlocked == 1 && Stars >= 3;
}

public sealed class LevelProgressService
{
    public const int BoxCount = 11;
    public const int LevelsPerBox = 25;

    public IReadOnlyList<LevelProgress> Project(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var levels = new List<LevelProgress>(BoxCount * LevelsPerBox);
        var indexes = FirstEntryIndexes(save);

        for (var box = 0; box < BoxCount; box++)
        {
            for (var level = 0; level < LevelsPerBox; level++)
            {
                var progress = new LevelProgress(box, level)
                {
                    Score = GetOrDefault(save, indexes, $"SCORE_{box}_{level}"),
                    Stars = GetOrDefault(save, indexes, $"STARS_{box}_{level}"),
                    Unlocked = GetOrDefault(save, indexes, $"UNLOCKED_{box}_{level}")
                };
                levels.Add(progress);
            }
        }

        return levels;
    }

    public int SetAllUnlocked(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return SetExistingLevelValues(save, "UNLOCKED", 1);
    }

    public int SetAllStars(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return SetExistingLevelValues(save, "STARS", 3);
    }

    public int SetAllScores(CutTheRopeSave save, int score = 5_999)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (score is < 0 or > 5_999)
            throw new ArgumentOutOfRangeException(nameof(score));

        var changed = SetExistingLevelValues(save, "SCORE", score);
        new ScoreHashService().Recalculate(save);
        return changed;
    }

    public int SetAllCompleted(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var changed = 0;
        if (save.TryGetInt("PREFS_LEVELS_WON", out var levelsWon) && levelsWon != BoxCount * LevelsPerBox)
        {
            save.SetInt("PREFS_LEVELS_WON", BoxCount * LevelsPerBox);
            changed++;
        }

        if (save.TryGetInt("PREFS_LAST_PACK", out var lastPack) && lastPack != BoxCount - 1)
        {
            save.SetInt("PREFS_LAST_PACK", BoxCount - 1);
            changed++;
        }

        return changed;
    }

    private static Dictionary<string, int> FirstEntryIndexes(CutTheRopeSave save)
    {
        // Entries are publicly mutable, so this index belongs only to one operation.
        // TryAdd retains the same first-duplicate behavior as TryGetInt / SetInt.
        var indexes = new Dictionary<string, int>(save.IntEntries.Count, StringComparer.Ordinal);
        for (var index = 0; index < save.IntEntries.Count; index++)
            indexes.TryAdd(save.IntEntries[index].Key, index);
        return indexes;
    }

    private static int SetExistingLevelValues(CutTheRopeSave save, string prefix, int value)
    {
        var indexes = FirstEntryIndexes(save);
        var changed = 0;
        for (var box = 0; box < BoxCount; box++)
        {
            for (var level = 0; level < LevelsPerBox; level++)
            {
                if (!indexes.TryGetValue($"{prefix}_{box}_{level}", out var index)) continue;
                var entry = save.IntEntries[index];
                if (entry.Value == value) continue;
                save.IntEntries[index] = entry with { Value = value };
                changed++;
            }
        }
        return changed;
    }

    private static int GetOrDefault(CutTheRopeSave save, IReadOnlyDictionary<string, int> indexes, string key) =>
        indexes.TryGetValue(key, out var index) ? save.IntEntries[index].Value : 0;
}
