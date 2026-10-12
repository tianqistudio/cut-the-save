using System.IO;
using System.Windows;
using System.Windows.Automation;
using CutTheSave.Core;

namespace CutTheSave.App;

public partial class SaveRecoveryWindow : Window
{
    private readonly BackupService _store;
    private readonly Func<bool>? _confirmDiscard;
    private InterruptedSaveStatus? _review;
    internal string? RestoredPath { get; private set; }

    internal SaveRecoveryWindow(BackupService store, Func<bool>? confirmDiscard = null)
    {
        InitializeComponent();
        _store = store;
        _confirmDiscard = confirmDiscard;
        Title = TitleText.Text = LocalizationService.Get("SaveRecoveryTitle");
        HelpText.Text = LocalizationService.Get("SaveRecoveryHelp");
        foreach (var item in new[] { (RefreshButton, "RefreshBackups", "TipRefreshBackups"),
            (KeepButton, "KeepCurrentSave", "TipKeepCurrentSave"), (RestoreButton, "RecoverPreviousSave", "TipRecoverPreviousSave"),
            (CloseButton, "Close", "Close") })
        {
            item.Item1.Content = LocalizationService.Get(item.Item2);
            item.Item1.ToolTip = LocalizationService.Get(item.Item3);
            AutomationProperties.SetName(item.Item1, LocalizationService.Get(item.Item2));
        }
        RefreshStatus();
    }

    internal void RefreshStatus()
    {
        try
        {
            _review = _store.InspectInterruptedSave();
            TargetText.Text = _review.CurrentTarget?.Path ?? _review.JournalPath;
            StateText.Text = LocalizationService.Get(_review.State switch
            {
                InterruptedSaveState.None => "NoInterruptedSave",
                InterruptedSaveState.OriginalPresent => "InterruptedOriginalPresent",
                InterruptedSaveState.OutputPresent => "InterruptedOutputPresent",
                InterruptedSaveState.Changed => "InterruptedTargetChanged",
                _ => "InterruptedNeedsReview"
            });
            EvidenceText.Text = LocalizationService.Get("RecoveryEvidenceHelp");
            if (_review.RetainedPaths is { Count: > 0 })
                EvidenceText.Text += Environment.NewLine + LocalizationService.Get("RecoveryArtifactsRetained");
            EvidenceText.ToolTip = _review.JournalPath;
            KeepButton.IsEnabled = _review.CanKeepCurrent;
            RestoreButton.IsEnabled = _review.CanRestorePrevious;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            KeepButton.IsEnabled = RestoreButton.IsEnabled = false;
            StatusText.Text = LocalizationService.Format("BackupOperationFailed", MainWindow.DescribeSaveFailure(exception));
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();
    private void KeepButton_Click(object sender, RoutedEventArgs e) => Resolve(false, () => Confirm(false));
    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Resolve(true, () => Confirm(true));
    private bool Confirm(bool restore) => MessageBox.Show(this,
        LocalizationService.Format(restore ? "RecoverPreviousConfirm" : "KeepCurrentConfirm", _review?.CurrentTarget?.Path ?? "—"),
        Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    internal void Resolve(bool restore, Func<bool> confirm)
    {
        if (_review is not { CanKeepCurrent: true } review || restore && !review.CanRestorePrevious || !confirm()) return;
        if (restore && _confirmDiscard?.Invoke() == false) return;
        try
        {
            if (restore) SaveWriteGuard.EnsureGameClosed();
            var result = _store.ResolveInterruptedSave(review, restore);
            if (result.RestoredPrevious) RestoredPath = result.TargetPath;
            RefreshStatus();
            StatusText.Text = LocalizationService.Get(restore ? "RecoveredPreviousDone" : "KeptCurrentDone");
            if (result.RetainedPaths.Count > 0) StatusText.Text += " " + LocalizationService.Get("RecoveryArtifactsRetained");
            EvidenceText.ToolTip = result.ArchivePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RefreshStatus();
            StatusText.Text = LocalizationService.Format("BackupOperationFailed", MainWindow.DescribeSaveFailure(exception));
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
