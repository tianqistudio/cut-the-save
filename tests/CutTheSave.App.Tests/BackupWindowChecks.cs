using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CutTheSave.App;
using CutTheSave.Core;

internal static class BackupWindowChecks
{
    internal static void Run(string? previewDirectory = null)
    {
        var fixture = Directory.CreateTempSubdirectory("CutTheSave-backup-ui-");
        var originalLanguage = LocalizationService.Current;
        var originalTheme = AppearanceService.CurrentTheme;
        var originalMode = AppearanceService.SelectedColorMode;
        try
        {
            var target = Path.Combine(fixture.FullName, "ctr_save.bin");
            var store = new BackupService(Path.Combine(fixture.FullName, "backups"));
            var save = new CutTheRopeSave();
            save.SetInt("SCORE_0_0", 10);
            using (var stream = File.Create(target)) new SaveCodec().Write(stream, save);
            save.SetInt("SCORE_0_0", 20);
            new AtomicSaveService().Save(target, save, store.BackupDirectory);
            var snapshot = store.List(target).Single();

            if (previewDirectory is not null) Directory.CreateDirectory(previewDirectory);
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                IniLanguageChecks.Select(language, Path.Combine(fixture.FullName, "CutTheSave.ini"));
                CheckRecovery(fixture.FullName, language, previewDirectory);
                foreach (var theme in Enum.GetValues<AppVisualTheme>())
                foreach (var mode in new[] { AppColorMode.Light, AppColorMode.Dark })
                {
                    AppearanceService.ApplyForSession(theme, mode);
                    var window = new BackupsWindow(store, target, protectedPaths: [snapshot.Path]);
                    Check(window.BackupsGrid.Items.Count == 1, "pre-write snapshot is visible in the actual backup window");
                    Check(window.BackupsGrid.Items.Cast<BackupRecord>().Single().Reason == "pre-write", "UI uses indexed pre-write metadata");
                    window.BackupsGrid.SelectedItem = window.BackupsGrid.Items[0];
                    Check(window.LockButton.IsEnabled && window.CopyHashButton.IsEnabled && window.OpenFolderButton.IsEnabled,
                        "selecting a snapshot enables its metadata actions");
                    window.LockButton_Click(window.LockButton, new RoutedEventArgs());
                    Check(store.List(target).Single().IsLocked, "UI lock action persists protection");
                    Check((string)window.LockButton.Content == LocalizationService.Get("UnlockBackup"), "locked action changes to localized unlock");
                    window.LockButton_Click(window.LockButton, new RoutedEventArgs());
                    Check(!store.List(target).Single().IsLocked, "UI unlock action persists without deleting the snapshot");
                    foreach (var button in Buttons(window))
                        Check(button.ToolTip is not null && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), "backup action has tooltip and accessible name");
                    Check(window.SizeColumn.Header.ToString() == LocalizationService.Get("BackupSize"), "size metadata is localized");

                    // Display only synthetic public-shaped paths in persistent screenshots.
                    window.HelpText.Text = LocalizationService.Format("BackupsHelp", @"C:\Example\ctr_save.bin");
                    window.BackupsGrid.ItemsSource = new[] { snapshot with { OriginalPath = @"C:\Example\ctr_save.bin" } };
                    window.BackupsGrid.SelectedItem = window.BackupsGrid.Items[0];
                    var name = $"{theme}-{mode}-{LocalizationService.ToCode(language)}";
                    Render(window, 1084, 581, previewDirectory is null ? null : Path.Combine(previewDirectory, name + ".png"));
                    Render(window, 744, 381, previewDirectory is null ? null : Path.Combine(previewDirectory, name + "-minimum.png"));
                    window.Close();
                }
            }
            File.WriteAllText(Path.Combine(store.BackupDirectory, "backup-index.json"), "{ damaged");
            var damaged = new BackupsWindow(store, target);
            Check(!damaged.RestoreButton.IsEnabled && !damaged.CleanupButton.IsEnabled, "damaged index disables destructive UI actions");
            Check(damaged.StatusText.Text.Contains(LocalizationService.Format("BackupOperationFailed", string.Empty), StringComparison.Ordinal),
                "damaged index is reported instead of silently shown as empty");
            damaged.Close();
            Console.WriteLine("PASS backup UI: history, locks, recovery/cancel/conflicts, corruption handling, 24 normal and 12 recovery INI/theme/size layouts (synthetic only)");
        }
        finally
        {
            LocalizationService.UseLanguageForSession(originalLanguage);
            AppearanceService.ApplyForSession(originalTheme, originalMode);
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") fixture.Delete(recursive: true);
        }
    }

    private static Button[] Buttons(BackupsWindow window) =>
        [window.RefreshButton, window.RecoverButton, window.SaveRecoveryButton, window.CopyHashButton, window.OpenFolderButton, window.LockButton, window.CleanupButton, window.RestoreButton, window.CloseButton];

    private static void CheckRecovery(string root, AppLanguage language, string? previews)
    {
        foreach (var stop in new[] { BackupRetentionStage.SnapshotQuarantined, BackupRetentionStage.IndexCommitted })
        {
            var storePath = Path.Combine(root, "recovery-" + language + "-" + stop);
            var store = new BackupService(storePath);
            var target = Path.Combine(root, "recovery-save.bin");
            var save = new CutTheRopeSave();
            save.SetInt("SCORE_0_0", 10);
            using (var stream = File.Create(target)) new SaveCodec().Write(stream, save);
            store.CreateSnapshot(target, "first");
            store.CreateSnapshot(target, "second");
            var interrupted = new BackupService(storePath, stage => { if (stage == stop) throw new OperationCanceledException(); });
            try { interrupted.ApplyRetention(interrupted.PlanRetention(new BackupRetentionPolicy(1, long.MaxValue))); }
            catch (OperationCanceledException) { }
            var expectedState = stop == BackupRetentionStage.IndexCommitted ? BackupMaintenanceState.FinalizePending : BackupMaintenanceState.RollbackPending;
            Check(store.InspectMaintenance().State == expectedState, "isolated UI recovery fixture is pending");
            AppearanceService.ApplyForSession(AppVisualTheme.Windows11,
                stop == BackupRetentionStage.IndexCommitted ? AppColorMode.Dark : AppColorMode.Light);
            var window = new BackupsWindow(store, target);
            window.BackupsGrid.SelectedItem = window.BackupsGrid.Items[0];
            Check(window.RecoverButton.Visibility == Visibility.Visible && window.RecoverButton.IsEnabled, "pending recovery has an available action");
            Check(!window.RestoreButton.IsEnabled && !window.LockButton.IsEnabled && !window.CleanupButton.IsEnabled, "pending transaction disables mutations");
            Check((string)window.RecoverButton.Content == LocalizationService.Get("RepairBackupRecords")
                && window.StatusText.Text == LocalizationService.Get("BackupRepairPending"), "recovery action and guidance use INI language");
            window.RecoverMaintenance(() => false);
            Check(store.InspectMaintenance().State == expectedState, "cancel leaves the pending recovery untouched");
            window.HelpText.Text = LocalizationService.Format("BackupsHelp", @"C:\Example\ctr_save.bin");
            window.BackupsGrid.ItemsSource = store.List().Select(record => record with { OriginalPath = @"C:\Example\ctr_save.bin" }).ToArray();
            var name = $"recovery-{stop}-{LocalizationService.ToCode(language)}";
            Render(window, 1084, 581, previews is null ? null : Path.Combine(previews, name + ".png"));
            Render(window, 744, 381, previews is null ? null : Path.Combine(previews, name + "-minimum.png"));
            window.RecoverMaintenance(() => true);
            Check(store.InspectMaintenance().State == BackupMaintenanceState.None && window.RecoverButton.Visibility == Visibility.Collapsed,
                "production UI handler repairs and hides action");
            Check(window.StatusText.Text == LocalizationService.Get("BackupRepairDone") && window.CleanupButton.IsEnabled, "repair completion restores normal actions");
            // A malformed journal is retained, not offered as a clickable automatic fix.
            var journal = Path.Combine(storePath, "retention-transaction.json");
            File.WriteAllText(journal, "{ malformed");
            window.RefreshBackups();
            Check(!window.RecoverButton.IsEnabled && !window.CleanupButton.IsEnabled
                && window.StatusText.Text == LocalizationService.Get("BackupRepairReview"), "unverifiable journal requests review in selected language");
            Check(File.ReadAllText(journal) == "{ malformed", "opening conflict state is read-only");
            window.Close();
        }
    }

    private static void Render(BackupsWindow window, int width, int height, string? path)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content };
        host.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        foreach (var button in Buttons(window))
        {
            if (button.Visibility != Visibility.Visible) continue;
            var position = button.TranslatePoint(new Point(), host);
            Check(position.X >= 0 && position.Y >= 0 && position.X + button.ActualWidth <= width + 1
                && position.Y + button.ActualHeight <= height + 1, "backup action fits at minimum size");
        }
        Check(window.BackupsGrid.ActualHeight >= 50, "backup table retains usable height");
        if (path is not null)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path);
            encoder.Save(output);
        }
        host.Child = null;
        window.Content = content;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
