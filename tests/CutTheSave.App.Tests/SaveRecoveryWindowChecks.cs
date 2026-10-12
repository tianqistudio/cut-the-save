using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CutTheSave.App;
using CutTheSave.Core;

internal static class SaveRecoveryWindowChecks
{
    internal static void Run(string? previewDirectory = null)
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-save-recovery-ui-");
        var oldLanguage = LocalizationService.Current;
        var oldTheme = AppearanceService.CurrentTheme;
        var oldMode = AppearanceService.SelectedColorMode;
        try
        {
            if (previewDirectory is not null) Directory.CreateDirectory(previewDirectory);
            foreach (var language in Enum.GetValues<AppLanguage>())
            foreach (var theme in Enum.GetValues<AppVisualTheme>())
            foreach (var mode in new[] { AppColorMode.Light, AppColorMode.Dark })
            foreach (var state in new[] { "output", "original", "changed", "corrupt" })
            {
                IniLanguageChecks.Select(language, Path.Combine(root.FullName, "CutTheSave.ini"));
                AppearanceService.ApplyForSession(theme, mode);
                var name = $"{theme}-{mode}-{LocalizationService.ToCode(language)}-{state}";
                var fixture = Directory.CreateDirectory(Path.Combine(root.FullName, name));
                var target = Path.Combine(fixture.FullName, "save.bin");
                var store = new BackupService(Path.Combine(fixture.FullName, "backups"));
                Write(target, 10);
                try
                {
                    new AtomicSaveService(stage => { if (stage == SaveWriteStage.BeforeCleanup) throw new OperationCanceledException(); })
                        .Save(target, Save(20), store.BackupDirectory);
                }
                catch (OperationCanceledException) { }
                if (state == "changed") Write(target, 99);
                if (state == "original") Write(target, 10);
                if (state == "corrupt") File.WriteAllText(Path.Combine(store.BackupDirectory, "save-transaction.json"), "{ corrupt");
                var before = File.ReadAllBytes(target);
                var parent = new BackupsWindow(store, target);
                Check(parent.SaveRecoveryButton.Visibility == Visibility.Visible && !parent.CleanupButton.IsEnabled,
                    "pending save is reachable even when normal backup mutations are disabled");
                parent.Close();
                var discardCalls = 0;
                var window = new SaveRecoveryWindow(store, () => { discardCalls++; return true; });
                Check(window.KeepButton.IsEnabled == (state != "corrupt") && window.RestoreButton.IsEnabled == (state == "output"),
                    "only verified recovery choices are enabled");
                Check(window.Title == LocalizationService.Get("SaveRecoveryTitle"), "recovery title follows INI language");
                foreach (var button in Buttons(window)) Check(button.ToolTip is not null && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), "recovery tooltip/accessibility name");
                window.Resolve(false, () => false);
                Check(File.ReadAllBytes(target).SequenceEqual(before) && store.InspectInterruptedSave().State != InterruptedSaveState.None,
                    "cancel leaves both current save and pending journal unchanged");
                window.TargetText.Text = @"C:\Example\SavedGames\CutTheRope\ctr_save.bin";
                Render(window, 804, 491, previewDirectory is null ? null : Path.Combine(previewDirectory, name + ".png"));
                Render(window, 624, 381, previewDirectory is null ? null : Path.Combine(previewDirectory, name + "-minimum.png"));
                if (state == "corrupt")
                {
                    var asked = false;
                    window.Resolve(false, () => { asked = true; return true; });
                    Check(!asked && store.InspectInterruptedSave().State == InterruptedSaveState.NeedsReview, "invalid journal is not offered a destructive confirmation");
                }
                else
                {
                    var restore = state == "output" && mode == AppColorMode.Dark;
                    window.Resolve(restore, () => true);
                    Check(store.InspectInterruptedSave().State == InterruptedSaveState.None, "production recovery handler completes");
                    Check(store.List().All(record => record.IsLocked) && store.List().Count == (restore ? 2 : 1),
                        "UI resolution protects prior backup and records current output before restoring");
                    Check(discardCalls == (restore ? 1 : 0), "only replacing save content asks to leave the editor draft");
                    Check(Score(target) == (restore || state == "original" ? 10 : state == "changed" ? 99 : 20), "UI choice preserves exact intended target content");
                    Check(!window.KeepButton.IsEnabled && !window.RestoreButton.IsEnabled, "resolved actions cannot be accidentally repeated");
                }
                window.Close();
            }
            CheckStaleConfirmation(root.FullName);
            Console.WriteLine("PASS save recovery UI: INI languages, both themes/modes, four states, 96 layouts, safe actions/cancel, draft guards and stale confirmation");
        }
        finally
        {
            LocalizationService.UseLanguageForSession(oldLanguage);
            AppearanceService.ApplyForSession(oldTheme, oldMode);
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true);
        }
    }

    private static void CheckStaleConfirmation(string root)
    {
        var target = Path.Combine(root, "stale.bin");
        var store = new BackupService(Path.Combine(root, "stale-backups"));
        Write(target, 10);
        try { new AtomicSaveService(stage => { if (stage == SaveWriteStage.BeforeCleanup) throw new OperationCanceledException(); }).Save(target, Save(20), store.BackupDirectory); }
        catch (OperationCanceledException) { }
        var window = new SaveRecoveryWindow(store);
        window.Resolve(true, () => { Write(target, 99); return true; });
        Check(Score(target) == 99 && store.InspectInterruptedSave().State == InterruptedSaveState.Changed, "file changes during confirmation stop recovery");
        Check(window.StatusText.Text.Contains(LocalizationService.Get("SaveTargetChanged"), StringComparison.Ordinal), "stale recovery is explained in selected language");
        window.Close();
    }

    private static Button[] Buttons(SaveRecoveryWindow window) => [window.RefreshButton, window.KeepButton, window.RestoreButton, window.CloseButton];
    private static void Render(SaveRecoveryWindow window, int width, int height, string? path)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content };
        host.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
        host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
        host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
        foreach (var button in Buttons(window))
        {
            var point = button.TranslatePoint(new Point(), host);
            Check(point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= width + 1 && point.Y + button.ActualHeight <= height + 1,
                "all recovery actions fit the minimum window");
        }
        if (path is not null)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path); encoder.Save(output);
        }
        host.Child = null; window.Content = content;
    }
    private static CutTheRopeSave Save(int score) { var save = new CutTheRopeSave(); save.SetInt("SCORE_0_0", score); return save; }
    private static void Write(string path, int score) { using var output = File.Create(path); new SaveCodec().Write(output, Save(score)); }
    private static int Score(string path) { using var input = File.OpenRead(path); return new SaveCodec().Read(input).IntEntries.Single().Value; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
