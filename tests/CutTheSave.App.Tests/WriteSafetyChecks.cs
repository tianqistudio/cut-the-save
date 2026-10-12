using System.IO;
using CutTheSave.App;
using CutTheSave.Core;

internal static class WriteSafetyChecks
{
    internal static void Run(MainWindow window)
    {
        var language = LocalizationService.Current;
        var root = Directory.CreateTempSubdirectory("CutTheSave-write-ui-");
        try
        {
            var valid = Save(10);
            var path = Path.Combine(root.FullName, "save.bin");
            using (var output = File.Create(path)) new SaveCodec().Write(output, valid);
            var version = SaveTargetState.Capture(path);
            var duplicate = Save(10);
            duplicate.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 10));
            var warning = Save(10);
            warning.SetString("PREFS_SCORE_HASH", "missing terminator");
            var invalid = Save(6000);
            foreach (var selected in Enum.GetValues<AppLanguage>())
            {
                IniLanguageChecks.Select(selected, Path.Combine(root.FullName, "CutTheSave.ini"));
                foreach (var readOnly in new[] { duplicate, warning, invalid, new CutTheRopeSave() })
                {
                    window.AcceptLoadedSave(readOnly, path, "synthetic-backup", version);
                    Check(!window.CanEditLoadedSave && !window.SaveAsButton.IsEnabled && !window.RestoreOpenButton.IsEnabled,
                        "unsafe opened saves cannot be saved or restored via the main window");
                    Check(window.EntriesGrid.IsReadOnly && window.LevelsGrid.IsReadOnly && !window.UnlockAllButton.IsEnabled,
                        "both grids and batch actions are actually read-only");
                    Check(window.ValidationText.Text == LocalizationService.Get("SaveReadOnly")
                        && window.ValidationText.ToolTip.ToString()!.Contains(LocalizationService.Get("SaveReadOnlyHelp"), StringComparison.Ordinal),
                        "read-only status and reason are localized");
                    Check(window.ApplyBatchToDraft(save => new LevelProgressService().SetAllScores(save)) is null,
                        "production batch entry point cannot bypass read-only");
                    if (readOnly.IntEntries.Count > 0)
                    {
                        var raw = window.EntriesGrid.ItemsSource.Cast<MainWindow.SaveEntryRow>().First();
                        var level = window.LevelsGrid.ItemsSource.Cast<MainWindow.LevelRow>().First();
                        Check(!window.TryApplyRawEdit(raw, "20") && !window.TryApplyLevelEdit(level, 4, "20"),
                            "production edit handlers cannot bypass read-only");
                    }
                    Check(!window.HasUnsavedChanges, "blocked read-only actions preserve the loaded draft");
                }
                Check(MainWindow.DescribeSaveFailure(new SaveTargetChangedException()) == LocalizationService.Get("SaveTargetChanged"),
                    "external-change explanation follows the selected language");
                foreach (var outcome in Enum.GetValues<SaveRecoveryOutcome>())
                {
                    var key = outcome switch
                    {
                        SaveRecoveryOutcome.OriginalRestored => "WriteFailedRestored",
                        SaveRecoveryOutcome.CurrentFileRetained => "WriteFailedRetained",
                        _ => "WriteRecoveryReview"
                    };
                    var failure = new SaveWriteFailureException(new IOException("synthetic"), outcome, ["synthetic-recovery"]);
                    Check(MainWindow.DescribeSaveFailure(failure).Contains(LocalizationService.Get(key), StringComparison.Ordinal),
                        "recovery outcomes have distinct localized explanations");
                }
            }
            window.AcceptLoadedSave(valid, path, diskVersion: version);
            Check(window.CanEditLoadedSave && !window.EntriesGrid.IsReadOnly, "opening a safe save re-enables editing");
            var row = window.EntriesGrid.ItemsSource.Cast<MainWindow.SaveEntryRow>().Single();
            Check(window.TryApplyRawEdit(row, "30") && window.HasUnsavedChanges, "editable draft created for conflict test");
            var confirmations = 0;
            Check(window.ReviewSaveDestination(path, _ => { confirmations++; return false; }) is null && confirmations == 1,
                "declining overwrite preserves the draft");
            var newPath = Path.Combine(root.FullName, "new.bin");
            var fresh = window.ReviewSaveDestination(newPath, _ => throw new InvalidOperationException("No overwrite question expected"));
            Check(fresh is { Exists: false }, "a new path records an absent destination");
            using (var output = File.Create(path)) new SaveCodec().Write(output, Save(20));
            Throws<SaveTargetChangedException>(() => window.ReviewSaveDestination(path, _ => throw new InvalidOperationException("Conflict must stop before confirmation")));
            Check(window.HasUnsavedChanges && window.BuildEditedSave(out _).IntEntries.Single().Value == 30,
                "external file change does not discard the user's draft");
            var reviewed = window.ReviewSaveDestination(newPath, _ => true)!;
            var written = new AtomicSaveService().Save(newPath, window.BuildEditedSave(out _), Path.Combine(root.FullName, "backups"), expectedTarget: reviewed);
            window.AcceptSavedDraft(window.BuildEditedSave(out _), newPath, new SaveTargetState(written.Path, true, written.Length, written.Sha256));
            Check(!window.HasUnsavedChanges, "a verified successful save updates the baseline");
            using (var output = File.Create(newPath)) new SaveCodec().Write(output, Save(40));
            Throws<SaveTargetChangedException>(() => window.ReviewSaveDestination(newPath, _ => true));
            Console.WriteLine("PASS write UI: read-only grids/actions, warnings/errors, three-language recovery messages, overwrite review, conflicts and saved baseline (synthetic only)");
        }
        finally
        {
            window.AcceptLoadedSave(Save(10), "synthetic-only.bin");
            LocalizationService.UseLanguageForSession(language);
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true);
        }
    }

    private static CutTheRopeSave Save(int score) { var save = new CutTheRopeSave(); save.SetInt("SCORE_0_0", score); return save; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
