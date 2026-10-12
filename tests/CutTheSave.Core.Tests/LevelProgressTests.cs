using CutTheSave.Core;

internal static class LevelProgressTests
{
    internal static void Run()
    {
        ProjectionPreservesSparseAndDuplicateSemantics();
        BatchOperationsPreserveUnknownFieldsAndOrder();
        IndexDoesNotOutliveAnOperation();
        MaximumEntrySaveMatchesLinearReference();
    }

    private static void ProjectionPreservesSparseAndDuplicateSemantics()
    {
        var save = SparseSave();
        var before = Serialize(save);
        var levels = new LevelProgressService().Project(save);
        Check(levels.Count == 275, "all supported levels are projected");
        Check(levels[0].Score == 31 && levels[0].Stars == 1 && levels[0].Unlocked == 0,
            "first exact duplicate wins and missing fields remain zero");
        Check(levels[^1].BoxIndex == 10 && levels[^1].LevelIndex == 24 && levels[^1].Score == 52,
            "last supported level retains its position and value");
        Check(before.SequenceEqual(Serialize(save)), "projection leaves every source byte unchanged");
    }

    private static void BatchOperationsPreserveUnknownFieldsAndOrder()
    {
        var service = new LevelProgressService();
        foreach (var prefix in new[] { "SCORE", "STARS", "UNLOCKED" })
        {
            var save = SparseSave();
            var expected = Clone(save);
            var value = prefix == "SCORE" ? 1234 : prefix == "STARS" ? 3 : 1;
            var expectedCount = SetReferenceValues(expected, prefix, value);
            var actualCount = prefix switch
            {
                "SCORE" => service.SetAllScores(save, value),
                "STARS" => service.SetAllStars(save),
                _ => service.SetAllUnlocked(save)
            };
            Check(actualCount == expectedCount, "batch count includes only changed, existing exact keys");
            Check(Serialize(save).SequenceEqual(Serialize(expected)),
                "batch preserves sparse fields, later duplicates, unknown keys and entry order");
            var repeated = prefix switch
            {
                "SCORE" => service.SetAllScores(save, value),
                "STARS" => service.SetAllStars(save),
                _ => service.SetAllUnlocked(save)
            };
            Check(repeated == 0, "an already-applied batch reports no changed values");
        }
    }

    private static void IndexDoesNotOutliveAnOperation()
    {
        var save = SparseSave();
        var service = new LevelProgressService();
        service.Project(save);
        save.IntEntries.RemoveAt(0);
        save.IntEntries.Reverse();
        save.IntEntries.Insert(0, new SaveIntEntry("SCORE_0_0", 789));
        Check(service.Project(save)[0].Score == 789, "projection sees direct list edits after a previous call");
        Check(service.SetAllScores(save, 456) == 2 && save.IntEntries[0].Value == 456,
            "batch rebuilds first-entry positions after insertion and reordering");
        Check(save.IntEntries.Count(entry => entry.Key == "SCORE_0_0" && entry.Value == 99) == 1,
            "a later duplicate stays unchanged after list reordering");
    }

    private static void MaximumEntrySaveMatchesLinearReference()
    {
        var save = new CutTheRopeSave();
        for (var index = 0; index < SaveCodec.DefaultMaximumEntryCount - 825; index++)
            save.IntEntries.Add(new SaveIntEntry($"UNKNOWN_{index}", index));
        for (var box = 0; box < LevelProgressService.BoxCount; box++)
        {
            for (var level = 0; level < LevelProgressService.LevelsPerBox; level++)
            {
                save.IntEntries.Add(new SaveIntEntry($"SCORE_{box}_{level}", box * 25 + level));
                save.IntEntries.Add(new SaveIntEntry($"STARS_{box}_{level}", level % 4));
                save.IntEntries.Add(new SaveIntEntry($"UNLOCKED_{box}_{level}", box % 2));
            }
        }
        var expected = Clone(save);
        var service = new LevelProgressService();
        var projected = service.Project(save);
        foreach (var level in projected)
        {
            Check(save.TryGetInt(level.ScoreKey, out var score) && score == level.Score,
                "maximum-size score projection matches linear first-match lookup");
            Check(save.TryGetInt(level.StarsKey, out var stars) && stars == level.Stars,
                "maximum-size stars projection matches linear first-match lookup");
            Check(save.TryGetInt(level.UnlockedKey, out var unlocked) && unlocked == level.Unlocked,
                "maximum-size unlocked projection matches linear first-match lookup");
        }
        Check(service.SetAllStars(save) == SetReferenceValues(expected, "STARS", 3), "maximum-size stars count");
        Check(service.SetAllUnlocked(save) == SetReferenceValues(expected, "UNLOCKED", 1), "maximum-size unlock count");
        Check(service.SetAllScores(save) == SetReferenceValues(expected, "SCORE", 5999), "maximum-size score count");
        Check(Serialize(save).SequenceEqual(Serialize(expected)), "maximum-size mixed batches preserve exact serialization and score hash");
    }

    private static CutTheRopeSave SparseSave()
    {
        var save = new CutTheRopeSave();
        save.IntEntries.AddRange([
            new("SCORE_0_0", 31), new("SCORE_0_0", 99), new("STARS_0_0", 1),
            new("UNLOCKED_10_24", 0), new("SCORE_10_24", 52), new("SCORE_11_0", 17),
            new("SCORE_0_25", 19), new("SCORE_00_0", 23), new("score_0_0", 29),
            new("UNKNOWN", 71), new("STARS_0_0", 2)
        ]);
        save.StringEntries.Add(new("UNKNOWN_TEXT", "简体 / English / \0"));
        save.StringEntries.Add(new("PREFS_SCORE_HASH", "stale\0"));
        save.StringEntries.Add(new("PREFS_SCORE_HASH", "later duplicate\0"));
        return save;
    }

    private static int SetReferenceValues(CutTheRopeSave save, string prefix, int value)
    {
        var changed = 0;
        for (var box = 0; box < LevelProgressService.BoxCount; box++)
        {
            for (var level = 0; level < LevelProgressService.LevelsPerBox; level++)
            {
                var key = $"{prefix}_{box}_{level}";
                if (!save.TryGetInt(key, out var current) || current == value) continue;
                save.SetInt(key, value);
                changed++;
            }
        }
        if (prefix == "SCORE") new ScoreHashService().Recalculate(save);
        return changed;
    }

    private static CutTheRopeSave Clone(CutTheRopeSave save)
    {
        var clone = new CutTheRopeSave();
        clone.IntEntries.AddRange(save.IntEntries);
        clone.StringEntries.AddRange(save.StringEntries);
        return clone;
    }

    private static byte[] Serialize(CutTheRopeSave save)
    {
        using var stream = new MemoryStream();
        new SaveCodec().Write(stream, save);
        return stream.ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
