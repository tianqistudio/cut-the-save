using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CutTheSave.Core;

namespace CutTheSave.App;

public partial class MainWindow
{
    private CutTheRopeSave? _savedSaveSnapshot;
    private bool _skipCloseConfirmation;
    private SaveTargetState? _diskVersion;
    internal bool CanEditLoadedSave { get; private set; }

    internal bool HasUnsavedChanges => _loadedSave is not null && _savedSaveSnapshot is not null
        && (!_loadedSave.IntEntries.SequenceEqual(_savedSaveSnapshot.IntEntries)
            || !_loadedSave.StringEntries.SequenceEqual(_savedSaveSnapshot.StringEntries));

    internal void AcceptLoadedSave(CutTheRopeSave save, string path, string? openBackupPath = null, SaveTargetState? diskVersion = null)
    {
        CancelPendingGridEdits();
        _loadedSave = CloneSave(save);
        _openedSaveSnapshot = CloneSave(save);
        _savedSaveSnapshot = CloneSave(save);
        _loadedPath = path;
        _diskVersion = diskVersion;
        CanEditLoadedSave = _validator.Validate(save).CanWrite;
        _openBackupPath = openBackupPath;
        SetDisplayedSavePath(path);
        RestoreOpenButton.IsEnabled = openBackupPath is not null && CanEditLoadedSave;
        IntCountText.Text = save.IntEntries.Count.ToString(CultureInfo.InvariantCulture);
        StringCountText.Text = save.StringEntries.Count.ToString(CultureInfo.InvariantCulture);
        RefreshEntryGrid();
    }

    internal bool TryCommitGridEdits()
    {
        foreach (var grid in new[] { EntriesGrid, LevelsGrid })
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                SetStatus("FinishCellEdit");
                return false;
            }
        }
        return true;
    }

    private void CancelPendingGridEdits()
    {
        foreach (var grid in new[] { EntriesGrid, LevelsGrid })
        {
            grid.CancelEdit(DataGridEditingUnit.Cell);
            grid.CancelEdit(DataGridEditingUnit.Row);
        }
    }

    internal void AcceptSavedDraft(CutTheRopeSave saved, string path, SaveTargetState? diskVersion = null)
    {
        _loadedSave = CloneSave(saved);
        _savedSaveSnapshot = CloneSave(saved);
        _loadedPath = path;
        _diskVersion = diskVersion;
        RefreshEntryGrid();
    }

    internal int? ApplyBatchToDraft(Func<CutTheRopeSave, int> operation)
    {
        if (_loadedSave is null || !CanEditLoadedSave || !TryCommitGridEdits())
            return null;
        var draft = BuildEditedSave(out _);
        var changed = operation(draft);
        _loadedSave = draft;
        RefreshEntryGrid();
        return changed;
    }

    internal bool CanDiscardEdits(Func<bool> confirm)
    {
        var committed = TryCommitGridEdits();
        return (committed && !HasUnsavedChanges) || confirm();
    }

    private bool ConfirmDiscardEdits() => CanDiscardEdits(() =>
        MessageBox.Show(this, LocalizationService.Get("DiscardEditsConfirm"),
            LocalizationService.Get("UnsavedEditsTitle"), MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (IsOperationBusy) { e.Cancel = true; SetStatus("OperationBusy"); return; }
        if (!_skipCloseConfirmation && !ConfirmDiscardEdits())
            e.Cancel = true;
    }

    internal bool TryApplyRawEdit(SaveEntryRow row, string text)
    {
        if (_loadedSave is null || !CanEditLoadedSave)
            return false;
        if (text == row.Value)
            return true;
        var draft = CloneSave(_loadedSave);
        var scoreChanged = false;
        if (row.Kind == "Int32")
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                SetStatus("LevelInteger");
                return false;
            }
            var original = draft.IntEntries[row.SourceIndex];
            if (original.Key != row.Key)
                return false;
            draft.IntEntries[row.SourceIndex] = original with { Value = value };
            scoreChanged = original.Value != value && original.Key.StartsWith("SCORE_", StringComparison.Ordinal);
        }
        else
        {
            var original = draft.StringEntries[row.SourceIndex];
            if (original.Key != row.Key)
                return false;
            draft.StringEntries[row.SourceIndex] = original with { Value = text.Replace("\\0", "\0", StringComparison.Ordinal) };
        }

        if (!AcceptEditedDraft(draft, scoreChanged))
            return false;
        SynchronizeEditorRows();
        SetStatus("GridChanged");
        return true;
    }

    internal bool TryApplyLevelEdit(LevelRow row, int displayIndex, string text)
    {
        if (_loadedSave is null || !CanEditLoadedSave)
            return false;
        var key = displayIndex switch { 2 => row.UnlockedKey, 3 => row.StarsKey, 4 => row.ScoreKey, _ => null };
        if (key is null || !_loadedSave.TryGetInt(key, out var before))
        {
            SetStatus("MissingLevelField");
            return false;
        }
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            SetStatus("LevelInteger");
            return false;
        }
        var draft = CloneSave(_loadedSave);
        draft.SetInt(key, value);
        if (!AcceptEditedDraft(draft, displayIndex == 4 && before != value))
            return false;
        SynchronizeEditorRows();
        SetStatus("LevelChanged");
        return true;
    }

    private bool AcceptEditedDraft(CutTheRopeSave draft, bool scoreChanged)
    {
        var validation = _validator.Validate(draft);
        if (!validation.CanWrite)
        {
            SetStatus("EditRejected");
            return false;
        }
        // Preserve unknown layouts: do not add a hash field that was absent on open.
        if (scoreChanged && draft.TryGetString("PREFS_SCORE_HASH", out _))
            new ScoreHashService().Recalculate(draft);
        _loadedSave = draft;
        RenderValidation(_validator.Validate(draft));
        RefreshDirtyIndicator();
        return true;
    }

    private void SynchronizeEditorRows()
    {
        if (_loadedSave is null)
            return;
        foreach (var row in EntriesGrid.ItemsSource.Cast<SaveEntryRow>())
        {
            row.Value = row.Kind == "Int32"
                ? _loadedSave.IntEntries[row.SourceIndex].Value.ToString(CultureInfo.InvariantCulture)
                : _loadedSave.StringEntries[row.SourceIndex].Value.Replace("\0", "\\0", StringComparison.Ordinal);
        }
        foreach (var row in LevelsGrid.ItemsSource.Cast<LevelRow>())
        {
            if (_loadedSave.TryGetInt(row.UnlockedKey, out var unlocked)) row.SetValue(2, unlocked);
            if (_loadedSave.TryGetInt(row.StarsKey, out var stars)) row.SetValue(3, stars);
            if (_loadedSave.TryGetInt(row.ScoreKey, out var score)) row.SetValue(4, score);
        }
    }
}
