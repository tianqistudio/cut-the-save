using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using CutTheSave.App;
using CutTheSave.Core;

internal static class EditorSafetyChecks
{
    internal static void Run(MainWindow window)
    {
        var original = new CutTheRopeSave();
        original.SetInt("UNLOCKED_0_0", 0);
        original.SetInt("STARS_0_0", 1);
        original.SetInt("SCORE_0_0", 100);
        original.SetInt("UNLOCKED_0_1", 0);
        original.SetString("PREFS_SCORE_HASH", new ScoreHashService().Compute(100));
        original.SetString("custom-note", "keep");
        original.SetString("literal-backslash", @"literal\0-text");
        const string syntheticPath = @"C:\synthetic-only\ctr_save.bin";
        window.AcceptLoadedSave(original, syntheticPath);
        Check(!window.HasUnsavedChanges, "a loaded synthetic save starts clean");

        var rawRows = window.EntriesGrid.ItemsSource.Cast<MainWindow.SaveEntryRow>().ToArray();
        var levelRows = window.LevelsGrid.ItemsSource.Cast<MainWindow.LevelRow>().ToArray();
        var rawScore = rawRows.Single(row => row.Key == "SCORE_0_0");
        var rawStars = rawRows.Single(row => row.Key == "STARS_0_0");
        var rawNote = rawRows.Single(row => row.Key == "custom-note");
        var literalRow = rawRows.Single(row => row.Key == "literal-backslash");
        Check(window.TryApplyRawEdit(literalRow, literalRow.Value), "unchanged raw text commits without reinterpretation");
        Check(window.TryApplyRawEdit(rawNote, "unsaved note"), "raw text edits are accepted in memory");
        Check(window.TryApplyRawEdit(rawScore, "200"), "manual raw score edit is accepted");
        Check(levelRows[0].Score == 200 && levelRows[0].Changed != "No", "raw edit updates the existing level row and its changed marker");
        Check(ReferenceEquals(rawRows[0], window.EntriesGrid.ItemsSource.Cast<MainWindow.SaveEntryRow>().First()), "cell changes retain the existing row objects");
        Check(window.TryApplyLevelEdit(levelRows[0], 3, "2"), "level view accepts a valid star edit");
        Check(rawStars.Value == "2", "level edit updates the raw row");
        Check(window.TryApplyLevelEdit(levelRows[0], 4, "300"), "level view accepts a score edit");
        Check(rawScore.Value == "300", "level score is synchronized before Save as reads raw rows");

        var mixed = window.BuildEditedSave(out _);
        Check(Int(mixed, "SCORE_0_0") == 300 && Int(mixed, "STARS_0_0") == 2, "save projection retains both raw and level edits");
        Check(Text(mixed, "custom-note") == "unsaved note", "unrelated raw string edit survives level editing");
        Check(Text(mixed, "literal-backslash") == @"literal\0-text", "saving other edits does not convert a literal backslash-zero into NUL");
        Check(Text(mixed, "PREFS_SCORE_HASH") == new ScoreHashService().Compute(300), "manual scores recompute the existing hash");

        Check(!window.TryApplyLevelEdit(levelRows[0], 3, "4"), "out-of-range stars are rejected");
        Check(!window.TryApplyRawEdit(rawScore, "not-an-integer"), "invalid integer text is rejected");
        var invalidEvent = new DataGridCellEditEndingEventArgs(window.EntriesGrid.Columns[2],
            new DataGridRow { Item = rawScore }, new TextBox { Text = "not-an-integer" }, DataGridEditAction.Commit);
        window.EntriesGrid_CellEditEnding(window.EntriesGrid, invalidEvent);
        Check(invalidEvent.Cancel, "raw cell commit is cancelled when its text is invalid");
        var validEvent = new DataGridCellEditEndingEventArgs(window.LevelsGrid.Columns[3],
            new DataGridRow { Item = levelRows[0] }, new TextBox { Text = "2" }, DataGridEditAction.Commit);
        window.LevelsGrid_CellEditEnding(window.LevelsGrid, validEvent);
        Check(!validEvent.Cancel && rawStars.Value == "2", "the production level commit handler synchronizes raw rows");
        window.LevelsGrid.Columns[4].DisplayIndex = 2;
        var reorderedEvent = new DataGridCellEditEndingEventArgs(window.LevelsGrid.Columns[4],
            new DataGridRow { Item = levelRows[0] }, new TextBox { Text = "300" }, DataGridEditAction.Commit);
        window.LevelsGrid_CellEditEnding(window.LevelsGrid, reorderedEvent);
        Check(!reorderedEvent.Cancel && rawScore.Value == "300", "reordering a score column cannot edit a different field");
        window.LevelsGrid.Columns[4].DisplayIndex = 4;
        Check(!window.TryApplyLevelEdit(levelRows[2], 4, "500"), "missing level keys are not invented");
        Check(Int(window.BuildEditedSave(out _), "STARS_0_0") == 2, "rejected edits leave the model unchanged");

        Check(window.ApplyBatchToDraft(save => new LevelProgressService().SetAllUnlocked(save)) == 2, "batch edits modify only existing unlock entries");
        var batch = window.BuildEditedSave(out var changedCount);
        Check(changedCount >= 5, "saved-change count uses the disk baseline rather than already-edited model values");
        Check(Int(batch, "SCORE_0_0") == 300 && Int(batch, "STARS_0_0") == 2 && Text(batch, "custom-note") == "unsaved note",
            "batch operation cannot discard earlier manual edits");
        Check(Int(batch, "UNLOCKED_0_0") == 1 && Int(batch, "UNLOCKED_0_1") == 1, "batch unlock result is retained");
        Check(window.HasUnsavedChanges, "mixed and batch edits are marked unsaved");

        var prompts = 0;
        Check(!window.CanDiscardEdits(() => { prompts++; return false; }), "cancelled leave keeps the editor open");
        Check(window.HasUnsavedChanges && prompts == 1, "cancelled leave retains the draft and asks once");
        Check(window.CanDiscardEdits(() => true), "explicit discard permits leaving");
        Check(Text(window.BuildEditedSave(out _), "custom-note") == "unsaved note", "permission to leave does not itself destroy a draft");

        using var serialized = new MemoryStream();
        new SaveCodec().Write(serialized, batch);
        serialized.Position = 0;
        var reopened = new SaveCodec().Read(serialized);
        Check(Int(reopened, "SCORE_0_0") == 300 && Text(reopened, "custom-note") == "unsaved note", "serialized mixed edits reopen correctly");
        window.AcceptSavedDraft(reopened, syntheticPath);
        Check(!window.HasUnsavedChanges, "successful save establishes a new dirty baseline");
        Check(window.CanDiscardEdits(() => throw new InvalidOperationException("Clean saves must not prompt.")), "clean editor can leave without prompting");
        Check(window.LevelsGrid.ItemsSource.Cast<MainWindow.LevelRow>().First().Changed == "No", "changed marker clears after successful save");
        Check(Int(original, "SCORE_0_0") == 100 && Text(original, "custom-note") == "keep", "input fixture remains untouched");

        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            LocalizationService.UseLanguageForSession(language);
            SaveWriteGuard.EnsureGameClosed(() => false);
            ExpectBlocked(() => SaveWriteGuard.EnsureGameClosed(() => true), "CloseGameBeforeSave");
            ExpectBlocked(() => SaveWriteGuard.EnsureGameClosed(() => throw new Win32Exception(5)), "GameStateUnknown");
        }
        LocalizationService.UseLanguageForSession(AppLanguage.EnUs);
        window.AcceptLoadedSave(original, syntheticPath);
        Console.WriteLine("PASS editor safety: mixed views, batch preservation, score hash, invalid edits, dirty/leave/saved baseline, and game-write guards (synthetic only)");
    }

    private static void ExpectBlocked(Action action, string key)
    {
        try { action(); }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message == LocalizationService.Get(key), "write guard explains the block in the selected language");
            return;
        }
        throw new InvalidOperationException("The save write guard did not block.");
    }

    private static int Int(CutTheRopeSave save, string key) => save.TryGetInt(key, out var value) ? value : throw new InvalidOperationException(key);
    private static string Text(CutTheRopeSave save, string key) => save.TryGetString(key, out var value) ? value : throw new InvalidOperationException(key);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
