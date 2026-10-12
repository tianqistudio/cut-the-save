using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using CutTheSave.Core;

namespace CutTheSave.App;

public partial class BackupsWindow : Window
{
    private readonly BackupService _backupService;
    private readonly string? _currentPath;
    private readonly Func<bool>? _confirmDiscard;
    private readonly string[] _protectedPaths;
    private BackupMaintenanceState _maintenanceState;
    private bool _saveRecoveryPending;
    internal string? RestoredPath { get; private set; }

    public BackupsWindow(string? currentPath = null, Func<bool>? confirmDiscard = null)
        : this(new BackupService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CutTheSave", "backups")), currentPath, confirmDiscard) { }

    internal BackupsWindow(BackupService backupService, string? currentPath = null,
        Func<bool>? confirmDiscard = null, IEnumerable<string>? protectedPaths = null)
    {
        InitializeComponent();
        _backupService = backupService;
        _currentPath = currentPath;
        _confirmDiscard = confirmDiscard;
        _protectedPaths = (protectedPaths ?? []).ToArray();
        BackupsGrid.SelectionChanged += (_, _) => UpdateSelectionActions();
        ApplyLanguage();
        RefreshBackups();
    }

    private void ApplyLanguage()
    {
        Title = LocalizationService.Get("BackupsTitle");
        TitleText.Text = Title;
        HelpText.Text = LocalizationService.Format("BackupsHelp", _currentPath ?? LocalizationService.Get("AllSaveFiles"));
        RetentionText.Text = LocalizationService.Get("BackupRetentionHelp");
        foreach (var item in new[]
        {
            (RefreshButton, "RefreshBackups", "TipRefreshBackups"),
            (RecoverButton, "RepairBackupRecords", "TipRepairBackupRecords"),
            (SaveRecoveryButton, "SaveRecoveryTitle", "SaveRecoveryHelp"),
            (RestoreButton, "RestoreBackup", "TipRestoreBackup"),
            (LockButton, "LockBackup", "TipLockBackup"),
            (CopyHashButton, "CopyBackupHash", "TipCopyBackupHash"),
            (OpenFolderButton, "OpenBackupFolder", "TipOpenBackupFolder"),
            (CleanupButton, "CleanupBackups", "TipCleanupBackups"),
            (CloseButton, "Close", "Close")
        })
        {
            item.Item1.Content = LocalizationService.Get(item.Item2);
            item.Item1.ToolTip = LocalizationService.Get(item.Item3);
            System.Windows.Automation.AutomationProperties.SetName(item.Item1, LocalizationService.Get(item.Item2));
        }
        DateColumn.Header = LocalizationService.Get("BackupDate");
        ReasonColumn.Header = LocalizationService.Get("BackupReason");
        ReasonColumn.Binding = new Binding(nameof(BackupRecord.Reason)) { Converter = new BackupReasonConverter() };
        OriginalColumn.Header = LocalizationService.Get("BackupOriginal");
        HashColumn.Header = LocalizationService.Get("BackupHash");
        SizeColumn.Header = LocalizationService.Get("BackupSize");
        LockedColumn.Header = LocalizationService.Get("BackupLocked");
    }

    private void UpdateSelectionActions()
    {
        var selected = BackupsGrid.SelectedItem as BackupRecord;
        RestoreButton.IsEnabled = LockButton.IsEnabled = CopyHashButton.IsEnabled = OpenFolderButton.IsEnabled =
            selected is not null && _maintenanceState == BackupMaintenanceState.None && !_saveRecoveryPending;
        var lockText = LocalizationService.Get(selected?.IsLocked == true ? "UnlockBackup" : "LockBackup");
        LockButton.Content = lockText;
        System.Windows.Automation.AutomationProperties.SetName(LockButton, lockText);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshBackups();

    internal void RefreshBackups(string? selectedPath = null)
    {
        try
        {
            var maintenance = _backupService.InspectMaintenance();
            _maintenanceState = maintenance.State;
            _saveRecoveryPending = _backupService.InspectInterruptedSave().State != InterruptedSaveState.None;
            SaveRecoveryButton.Visibility = _saveRecoveryPending ? Visibility.Visible : Visibility.Collapsed;
            RecoverButton.Visibility = _maintenanceState == BackupMaintenanceState.None ? Visibility.Collapsed : Visibility.Visible;
            RecoverButton.IsEnabled = !_saveRecoveryPending && _maintenanceState is BackupMaintenanceState.RollbackPending or BackupMaintenanceState.FinalizePending;
            StatusText.ToolTip = _maintenanceState == BackupMaintenanceState.None ? null : maintenance.JournalPath;
            var records = _backupService.List(_currentPath).OrderByDescending(record => record.CreatedAt).ToArray();
            BackupsGrid.ItemsSource = records;
            BackupsGrid.SelectedItem = records.FirstOrDefault(record => string.Equals(record.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
            CleanupButton.IsEnabled = !_saveRecoveryPending && _maintenanceState == BackupMaintenanceState.None && _backupService.List().Count > 0;
            StatusText.Text = _saveRecoveryPending ? LocalizationService.Get("SaveRecoveryPending") : _maintenanceState != BackupMaintenanceState.None
                ? LocalizationService.Get(_maintenanceState == BackupMaintenanceState.NeedsReview ? "BackupRepairReview" : "BackupRepairPending")
                : records.Length == 0 ? LocalizationService.Get("BackupEmpty") : LocalizationService.Format("BackupCount", records.Length);
            UpdateSelectionActions();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackupsGrid.ItemsSource = Array.Empty<BackupRecord>();
            CleanupButton.IsEnabled = RecoverButton.IsEnabled = false;
            UpdateSelectionActions();
            ShowFailure(exception);
        }
    }

    private void SaveRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SaveRecoveryWindow(_backupService, _confirmDiscard) { Owner = this };
        window.ShowDialog();
        if (window.RestoredPath is not null) RestoredPath = window.RestoredPath;
        RefreshBackups();
    }

    private void RecoverButton_Click(object sender, RoutedEventArgs e) => RecoverMaintenance(() =>
        MessageBox.Show(this, LocalizationService.Get("BackupRepairConfirm"), LocalizationService.Get("RepairBackupRecords"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);

    internal void RecoverMaintenance(Func<bool> confirm)
    {
        if (!confirm()) return;
        try
        {
            var result = _backupService.RecoverInterruptedRetention();
            RefreshBackups();
            StatusText.Text = LocalizationService.Get(result.State == BackupMaintenanceState.None ? "BackupRepairDone" : "BackupRepairReview");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ShowFailure(exception); }
    }

    internal void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not BackupRecord record) return;
        try
        {
            _backupService.SetLocked(record.Path, !record.IsLocked);
            RefreshBackups(record.Path);
            StatusText.Text = LocalizationService.Get(record.IsLocked ? "BackupUnlocked" : "BackupLockSaved");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ShowFailure(exception); }
    }

    private void CopyHashButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not BackupRecord record) return;
        try
        {
            Clipboard.SetText(_backupService.Verify(record.Path).Sha256);
            StatusText.Text = LocalizationService.Get("BackupHashCopied");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException) { ShowFailure(exception); }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not BackupRecord record) return;
        try
        {
            var verified = _backupService.Verify(record.Path);
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select," + verified.Path);
            Process.Start(start);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { ShowFailure(exception); }
    }

    private void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var plan = _backupService.PlanRetention(protectedPaths: _protectedPaths);
            if (plan.Candidates.Count == 0)
            {
                StatusText.Text = LocalizationService.Get("NoExpiredBackups");
                return;
            }
            if (MessageBox.Show(this, LocalizationService.Format("CleanupBackupsConfirm", plan.Candidates.Count),
                LocalizationService.Get("CleanupBackups"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var result = _backupService.ApplyRetention(plan);
            RefreshBackups();
            StatusText.Text = LocalizationService.Format("BackupCleanupDone", result.DeletedCount, result.Issues.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { ShowFailure(exception); }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not BackupRecord record) return;
        var operationRestored = false;
        try
        {
            var expectedTarget = SaveTargetState.Capture(record.OriginalPath);
            if (MessageBox.Show(this, LocalizationService.Format("BackupRestoreConfirm", record.OriginalPath),
                LocalizationService.Get("RestoreBackup"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (_confirmDiscard?.Invoke() == false) return;
            SaveWriteGuard.EnsureGameClosed();
            var restored = _backupService.Restore(record.Path, record.OriginalPath, expectedTarget: expectedTarget);
            operationRestored = true;
            RestoredPath = record.OriginalPath;
            // Protect the selected restore source as well as the parent's active open snapshot.
            var maintenance = _backupService.EnforceRetention(_protectedPaths.Append(record.Path));
            RefreshBackups(record.Path);
            StatusText.Text = LocalizationService.Get("BackupRestored");
            if (restored.RetainedTransactionPaths is { Count: > 0 })
                StatusText.Text += " " + LocalizationService.Format("SavedWithRecoveryFiles", string.Join(Environment.NewLine, restored.RetainedTransactionPaths));
            if (maintenance.Issues.Count > 0)
                StatusText.Text += " " + LocalizationService.Get("BackupMaintenanceNotice");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or UnsupportedSaveException)
        {
            RefreshBackups(record.Path);
            StatusText.Text = operationRestored
                ? LocalizationService.Get("BackupRestored") + " " + LocalizationService.Get("BackupMaintenanceNotice")
                : LocalizationService.Format("BackupOperationFailed", MainWindow.DescribeSaveFailure(exception));
        }
    }

    private void ShowFailure(Exception exception)
    {
        if (exception is BackupMaintenancePendingException or SaveRecoveryPendingException)
        {
            RefreshBackups();
            return;
        }
        StatusText.Text = LocalizationService.Format("BackupOperationFailed", MainWindow.DescribeSaveFailure(exception));
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class BackupReasonConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
        {
            "open" => LocalizationService.Get("BackupReasonOpen"),
            "pre-write" => LocalizationService.Get("BackupReasonPreWrite"),
            "recovery-pre-restore" => LocalizationService.Get("BackupReasonRestore"),
            _ => value
        };
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
