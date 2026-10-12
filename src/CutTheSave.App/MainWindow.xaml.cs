using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Windows.Controls;
using System.Windows;
using CutTheSave.Core;
using CutTheSave.Infrastructure.Windows;
using Microsoft.Win32;

namespace CutTheSave.App;

public partial class MainWindow : Window
{
    private readonly bool _suppressStartupDetection;
    private readonly SaveCodec _codec = new();
    private readonly SaveValidator _validator = new();
    private readonly LevelProgressService _levelProgress = new();
    private readonly SteamLocator _steamLocator = new();
    private readonly SaveLocator _saveLocator = new();
    private readonly RtssDetector _rtssDetector = new();
    private readonly RtssProfileService _rtssProfileService = new();
    private readonly RtssStateStore _rtssStateStore = new();
    private readonly RtssElevationBroker _rtssElevationBroker = new();
    private readonly UpdateCheckStateStore _updateCheckStateStore = new();
    private readonly UpdateConfiguration _updateConfiguration = UpdateConfiguration.Load();
    private readonly CancellationTokenSource _updateLifetime = new();
    private readonly BackupService _backupService;
    private CutTheRopeSave? _loadedSave;
    private CutTheRopeSave? _openedSaveSnapshot;
    private string? _loadedPath;
    private string? _selectedGameExecutable;
    private string? _openBackupPath;
    private RtssInstallation? _rtssInstallation;
    private string? _rtssBackupPath;
    private bool _rtssProfileWasCreated;
    private bool _startupUpdateCheckStarted;
    private bool _startupDetectionStarted;
    private string? _displayedSavePath;
    private Func<string> _statusTextFactory = () => LocalizationService.Get("Ready");
    private Func<string> _rtssStatusTextFactory = () => LocalizationService.Get("RtssNotDetected");
    private Func<string> _rtssProfileStatusTextFactory = () => LocalizationService.Get("RtssProfileNotRead");

    public MainWindow() : this(suppressStartupDetection: false) { }

    internal MainWindow(bool suppressStartupDetection)
    {
        _suppressStartupDetection = suppressStartupDetection;
        InitializeComponent();
        _backupService = new BackupService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CutTheSave", "backups"));
        SizeChanged += (_, _) => UpdateCompactLayout(ActualWidth, ActualHeight);
        ApplyLanguage();
        EntriesGrid.ItemsSource = new ObservableCollection<SaveEntryRow>();
        if (!suppressStartupDetection) RestorePersistedRtssState();
        UpdateRefreshRateAdvice();
        AppearanceService.AppearanceChanged += AppearanceService_AppearanceChanged;
        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _updateLifetime.Cancel();
            AppearanceService.AppearanceChanged -= AppearanceService_AppearanceChanged;
            _openBackupLease?.Dispose();
        };
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_suppressStartupDetection)
            return;
        if (!_startupDetectionStarted)
        {
            _startupDetectionStarted = true;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(RunStartupDetection));
        }

        if (_startupUpdateCheckStarted || !_updateConfiguration.IsReady || !_updateCheckStateStore.ShouldCheck(DateTimeOffset.UtcNow))
            return;

        _startupUpdateCheckStarted = true;
        try
        {
            var result = await CheckForUpdateAsync();
            if (_updateLifetime.IsCancellationRequested) return;
            if (result.HasValidSource && result.Decision.Kind != UpdateDecisionKind.SecurityConflict)
                _updateCheckStateStore.MarkSuccessful(DateTimeOffset.UtcNow);

            if (result.Decision.Kind == UpdateDecisionKind.Available && result.Decision.Manifest is not null)
                SetStatus("UpdateAvailable", result.Decision.Manifest.Version);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidDataException or IOException or InvalidOperationException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Startup update checks are best-effort and must never block save editing.
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new LanguageSelectionWindow(
            LocalizationService.Current,
            LocalizationService.RecommendForCurrentSystem(),
            CultureInfo.CurrentUICulture.Name);
        if (picker.ShowDialog() == true)
        {
            LocalizationService.SetLanguage(picker.SelectedLanguage);
            ApplyLanguage();
        }
    }

    private void AppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AppearanceWindow { Owner = this };
        if (picker.ShowDialog() == true)
            ApplyAppearanceText();
    }

    private void AppearanceService_AppearanceChanged(object? sender, EventArgs e)
    {
        ApplyAppearanceText();
        RefreshValidationText();
    }

    internal void ApplyLanguage()
    {
        TitleText.Text = "Cut the Save";
        SubtitleText.Text = LocalizationService.Get("Subtitle");
        LanguageButton.Content = LocalizationService.Get("LanguageButton");
        AppearanceButton.Content = LocalizationService.Get("AppearanceButton");
        ReadmeButton.Content = LocalizationService.Get("ReadmeButton");
        ReadmeButton.ToolTip = LocalizationService.Get("TipReadme");
        LanguageButton.ToolTip = LocalizationService.Get("TipLanguage");
        ApplyAppearanceText();
        SupportButton.Content = LocalizationService.Get("SupportButton");
        SupportButton.ToolTip = LocalizationService.Get("TipSupport");
        UpdateButton.Content = LocalizationService.Get("UpdateButton");
        UpdateButton.ToolTip = LocalizationService.Get("TipUpdate");
        GuideTitleText.Text = LocalizationService.Get("GuideTitle");
        GuideText.Text = LocalizationService.Get("GuideText");
        GuideActionButton.Content = LocalizationService.Get("GuideAction");
        GuideActionButton.ToolTip = LocalizationService.Get("TipDetect");
        SaveCardTitle.Text = LocalizationService.Get("SaveTitle");
        SaveCardHelp.Text = LocalizationService.Get("SaveHelp");
        DetectButton.Content = LocalizationService.Get("Detect");
        DetectButton.ToolTip = LocalizationService.Get("TipDetect");
        SelectGameButton.Content = LocalizationService.Get("SelectGame");
        SelectGameButton.ToolTip = LocalizationService.Get("TipSelectGame");
        LaunchGameButton.Content = LocalizationService.Get("LaunchGame");
        LaunchGameButton.ToolTip = LocalizationService.Get("TipLaunchGame");
        OpenButton.Content = LocalizationService.Get("Open");
        OpenButton.ToolTip = LocalizationService.Get("TipOpen");
        SaveAsButton.Content = LocalizationService.Get("SaveAs");
        SaveAsButton.ToolTip = LocalizationService.Get("TipSaveAs");
        RestoreOpenButton.Content = LocalizationService.Get("RestoreOpen");
        RestoreOpenButton.ToolTip = LocalizationService.Get("TipRestoreOpen");
        BackupsButton.Content = LocalizationService.Get("BackupsButton");
        BackupsButton.ToolTip = LocalizationService.Get("TipBackups");
        FilePathLabel.Text = LocalizationService.Get("CurrentPath");
        RtssTitleText.Text = LocalizationService.Get("SpeedTitle");
        RtssHelpText.Text = LocalizationService.Get("SpeedHelp");
        DetectRtssButton.Content = LocalizationService.Get("DetectRtss");
        DetectRtssButton.ToolTip = LocalizationService.Get("TipDetectRtss");
        GetRtssButton.Content = LocalizationService.Get("GetRtss");
        GetRtssButton.ToolTip = LocalizationService.Get("TipGetRtss");
        ReadRtssButton.Content = LocalizationService.Get("ReadProfile");
        ReadRtssButton.ToolTip = LocalizationService.Get("TipReadProfile");
        ApplyRtssButton.Content = LocalizationService.Get("Apply60");
        ApplyRtssButton.ToolTip = LocalizationService.Get("TipApply60");
        UndoRtssButton.Content = LocalizationService.Get("Undo");
        UndoRtssButton.ToolTip = LocalizationService.Get("TipUndoRtss");
        BatchTitleText.Text = LocalizationService.Get("BatchTitle");
        BatchHelpText.Text = LocalizationService.Get("BatchHelp");
        UnlockAllButton.Content = LocalizationService.Get("UnlockAll");
        StarsAllButton.Content = LocalizationService.Get("StarsAll");
        ScoresAllButton.Content = LocalizationService.Get("ScoresAll");
        CompleteAllButton.Content = LocalizationService.Get("CompleteAll");
        UnlockAllButton.ToolTip = LocalizationService.Get("TipBatch");
        StarsAllButton.ToolTip = LocalizationService.Get("TipBatch");
        ScoresAllButton.ToolTip = LocalizationService.Get("TipBatch");
        CompleteAllButton.ToolTip = LocalizationService.Get("TipComplete");
        UndoMemoryButton.Content = LocalizationService.Get("UndoMemory");
        UndoMemoryButton.ToolTip = LocalizationService.Get("TipUndoMemory");
        SummaryTitleText.Text = LocalizationService.Get("Summary");
        IntCountLabel.Text = LocalizationService.Get("IntCount");
        StringCountLabel.Text = LocalizationService.Get("StringCount");
        ValidationLabel.Text = LocalizationService.Get("Validation");
        EntriesTitleText.Text = LocalizationService.Get("Entries");
        EntriesGrid.Columns[0].Header = LocalizationService.Get("Type");
        EntriesGrid.Columns[1].Header = LocalizationService.Get("Key");
        EntriesGrid.Columns[2].Header = LocalizationService.Get("Value");
        EntriesTab.Header = LocalizationService.Get("RawEntries");
        LevelsTab.Header = LocalizationService.Get("LevelsView");
        LevelsGrid.Columns[0].Header = LocalizationService.Get("Box");
        LevelsGrid.Columns[1].Header = LocalizationService.Get("Level");
        LevelsGrid.Columns[2].Header = LocalizationService.Get("Unlocked");
        LevelsGrid.Columns[3].Header = LocalizationService.Get("Stars");
        LevelsGrid.Columns[4].Header = LocalizationService.Get("Score");
        LevelsGrid.Columns[5].Header = LocalizationService.Get("Changed");
        if (LevelsGrid.ItemsSource is not null)
            foreach (var row in LevelsGrid.ItemsSource.Cast<LevelRow>()) row.RefreshLanguage();
        RefreshDirtyIndicator();
        RefreshLocalizedDynamicText();
    }

    private void ApplyAppearanceText()
    {
        AppearanceButton.Content = LocalizationService.Get("AppearanceButton");
        AppearanceButton.ToolTip = LocalizationService.Format(
            "TipAppearance",
            LocalizationService.Get(AppearanceService.CurrentTheme == AppVisualTheme.Classic ? "ThemeClassic" : "ThemeWindows11"),
            LocalizationService.Get(AppearanceService.SelectedColorMode switch
            {
                AppColorMode.Light => "ColorLight",
                AppColorMode.Dark => "ColorDark",
                _ => "ColorSystem"
            }),
            LocalizationService.Get(AppearanceService.EffectiveColorMode == AppColorMode.Dark ? "ColorDark" : "ColorLight"));
    }

    private void RefreshLocalizedDynamicText()
    {
        RenderDisplayedSavePath();
        RefreshValidationText();
        StatusText.Text = _statusTextFactory();
        RtssStatusText.Text = _rtssStatusTextFactory();
        RtssProfileStatusText.Text = _rtssProfileStatusTextFactory();
        RefreshRtssCompatibility();
    }

    private void SetDisplayedSavePath(string? path)
    {
        _displayedSavePath = string.IsNullOrWhiteSpace(path) ? null : path;
        RenderDisplayedSavePath();
    }

    private void RenderDisplayedSavePath()
    {
        var text = _displayedSavePath ?? LocalizationService.Get("NoFileOpen");
        FilePathText.Text = text;
        FilePathText.ToolTip = text;
    }

    private void SetStatus(string key, params object?[] args)
    {
        var capturedArguments = args.ToArray();
        SetStatus(() => capturedArguments.Length == 0
            ? LocalizationService.Get(key)
            : LocalizationService.Format(key, capturedArguments));
    }

    private void SetStatus(Func<string> factory)
    {
        _statusTextFactory = factory;
        StatusText.Text = factory();
    }

    private void SetRtssStatus(string key, params object?[] args)
    {
        var capturedArguments = args.ToArray();
        SetRtssStatus(() => capturedArguments.Length == 0
            ? LocalizationService.Get(key)
            : LocalizationService.Format(key, capturedArguments));
    }

    private void SetRtssStatus(Func<string> factory)
    {
        _rtssStatusTextFactory = factory;
        RtssStatusText.Text = factory();
    }

    private void SetRtssProfileStatus(string key, params object?[] args)
    {
        var capturedArguments = args.ToArray();
        SetRtssProfileStatus(() => capturedArguments.Length == 0
            ? LocalizationService.Get(key)
            : LocalizationService.Format(key, capturedArguments));
    }

    private void SetRtssProfileStatus(Func<string> factory)
    {
        _rtssProfileStatusTextFactory = factory;
        RtssProfileStatusText.Text = factory();
    }

    private void RefreshValidationText()
    {
        if (_loadedSave is null)
        {
            ValidationText.Text = LocalizationService.Get("ValidationNotLoaded");
            ValidationText.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
            return;
        }

        RenderValidation(_validator.Validate(_loadedSave));
    }

    private void RenderValidation(SaveValidationResult validation)
    {
        ValidationText.Text = validation.CanWrite
            ? LocalizationService.Get("ValidationPass") : LocalizationService.Get("SaveReadOnly");
        ValidationText.ToolTip = validation.CanWrite ? null : LocalizationService.Get("SaveReadOnlyHelp")
            + Environment.NewLine + string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Code + ": " + issue.Message));
        ValidationText.SetResourceReference(
            ForegroundProperty,
            validation.CanWrite ? "SuccessBrush" : "DangerBrush");
    }

    private void ReadmeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ReadmeWindow { Owner = this };
        window.ShowDialog();
    }

    private async void BackupsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new BackupsWindow(_backupService, _loadedPath, ConfirmDiscardEdits,
            _openBackupPath is null ? [] : [_openBackupPath]) { Owner = this };
        window.ShowDialog();
        if (window.RestoredPath is not null && string.Equals(window.RestoredPath, _loadedPath, StringComparison.OrdinalIgnoreCase))
            await LoadSaveFileAsync(window.RestoredPath);
    }

    private async void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("SupportDialogTitle"),
            Filter = LocalizationService.Get("SupportFilter"),
            FileName = $"CutTheSave-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var refreshRate = GetPrimaryRefreshRate();
            var output = await RunOperationAsync(() => new SupportBundleService().Create(
                dialog.FileName,
                refreshRate,
                _steamLocator.FindCutTheRopeInstallations(),
                _rtssDetector.FindInstallations()));
            SetStatus("SupportCreated", output);
            if (MessageBox.Show(this, LocalizationService.Get("SupportOpenFolder"), LocalizationService.Get("SupportDoneTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{output}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus("SupportFailed", ex.Message);
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_updateConfiguration.IsReady)
        {
            SetStatus("UpdateNotConfigured");
            return;
        }

        UpdateButton.IsEnabled = false;
        SetStatus("UpdateChecking");
        var cancellationToken = _updateLifetime.Token;
        try
        {
            using var sourceClient = CreateUpdateClient();
            var verified = await sourceClient.FetchAllAsync(_updateConfiguration.Sources.Select(source =>
                (source.Id, source.ManifestUri, source.SignatureUri)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
            var decision = new UpdateDecisionService().Choose(currentVersion, verified);
            if (decision.Kind == UpdateDecisionKind.AlreadyCurrent)
            {
                SetStatus("UpdateNone");
                return;
            }
            if (decision.Kind != UpdateDecisionKind.Available || decision.Manifest is null)
            {
                var failure = decision.Message;
                SetStatus(() => LocalizationService.Format("UpdateFailed", failure ?? LocalizationService.Get("UpdateNone")));
                return;
            }

            var version = decision.Manifest.Version;
            if (MessageBox.Show(this, LocalizationService.Format("UpdateConfirm", version), LocalizationService.Get("UpdateConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                SetStatus("UpdateAvailable", version);
                return;
            }

            SetStatus("UpdateDownloading");
            var asset = decision.Manifest.Assets.FirstOrDefault(item => string.Equals(item.Rid, "win-x64", StringComparison.OrdinalIgnoreCase));
            if (asset is null)
                throw new InvalidOperationException("The signed manifest has no win-x64 asset.");
            var bytes = await sourceClient.DownloadAssetAsync(asset, cancellationToken);
            if (bytes is null)
                throw new InvalidOperationException("No allowed mirror returned a verified asset.");

            var signedSource = verified.Single(source => ReferenceEquals(source.Manifest, decision.Manifest));
            var updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CutTheSave", "updates");
            var updater = Path.Combine(AppContext.BaseDirectory, "CutTheSave.Updater.exe");
            if (!File.Exists(updater))
                throw new InvalidOperationException("CutTheSave.Updater.exe is missing from the application folder.");
            using var parent = Process.GetCurrentProcess();
            var parentStartTime = parent.StartTime.ToUniversalTime().Ticks;
            var prepared = await Task.Run(() => ReleaseUpdateTrust.CreateTransaction().Prepare(signedSource, bytes,
                AppContext.BaseDirectory, updateRoot, updater, Environment.ProcessId, parentStartTime, cancellationToken), cancellationToken);
            if (!ConfirmDiscardEdits())
                return;
            cancellationToken.ThrowIfCancellationRequested();
            using var workerLease = prepared.OpenWorkerLease();
            var start = new ProcessStartInfo(prepared.WorkerPath)
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(prepared.WorkerPath)! };
            foreach (var argument in new[] { "--install", prepared.RequestPath, "--request-sha256", prepared.RequestSha256 })
                start.ArgumentList.Add(argument);
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("The update worker did not start.");
            _skipCloseConfirmation = true;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidDataException or IOException or InvalidOperationException or HttpRequestException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("UpdateFailed", exception.Message);
        }
        finally
        {
            if (!Application.Current.Dispatcher.HasShutdownStarted)
                UpdateButton.IsEnabled = true;
        }
    }

    private async Task<(UpdateDecision Decision, bool HasValidSource)> CheckForUpdateAsync()
    {
        using var sourceClient = CreateUpdateClient();
        var verified = await sourceClient.FetchAllAsync(_updateConfiguration.Sources.Select(source =>
            (source.Id, source.ManifestUri, source.SignatureUri)), _updateLifetime.Token);

        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        var decision = new UpdateDecisionService().Choose(currentVersion, verified);
        return (decision, verified.Count > 0);
    }

    private UpdateSourceClient CreateUpdateClient() => new(
        new Ed25519ManifestVerifier(Convert.FromBase64String(_updateConfiguration.PublicKeyBase64!)),
        _updateConfiguration.AllowedHosts, _updateConfiguration.KeyId!, _updateConfiguration.Channel);

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private async void RunStartupDetection()
    {
        await DetectGameAndSaveAsync(allowInstallationPicker: false);
        DetectRtssButton_Click(this, new RoutedEventArgs());
    }

    private async void DetectButton_Click(object sender, RoutedEventArgs e) => await DetectGameAndSaveAsync(allowInstallationPicker: true);

    private async Task DetectGameAndSaveAsync(bool allowInstallationPicker)
    {
        if (!ConfirmDiscardEdits())
            return;
        try
        {
            if (await RunOperationAsync(() => _backupService.InspectInterruptedSave().State) != InterruptedSaveState.None)
            {
                SetStatus("SaveRecoveryPending");
                return;
            }
            var saveOverride = LocalizationService.LoadSaveOverride();
            var (installations, save) = await RunOperationAsync(() => (_steamLocator.FindCutTheRopeInstallations(), _saveLocator.GetDefaultLocation(saveOverride)));
            var game = installations.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(_selectedGameExecutable))
            {
                var selectedDirectory = Path.GetDirectoryName(_selectedGameExecutable) ?? string.Empty;
                game = game is null
                    ? new GameInstallation(selectedDirectory, selectedDirectory, _selectedGameExecutable)
                    : game with { GameDirectory = selectedDirectory, ExecutablePath = _selectedGameExecutable };
            }
            if (installations.Count > 1 && !allowInstallationPicker)
            {
                _selectedGameExecutable = null;
                LaunchGameButton.IsEnabled = false;
                SetDisplayedSavePath(save.Path);
                SetStatus("MultipleGames", installations.Count);
                return;
            }

            if (installations.Count > 1)
            {
                var picker = new GameInstallationPickerWindow(installations) { Owner = this };
                if (picker.ShowDialog() == true)
                    game = picker.SelectedInstallation;
                else
                {
                    _selectedGameExecutable = null;
                    LaunchGameButton.IsEnabled = false;
                    SetDisplayedSavePath(save.Path);
                    SetStatus("MultipleGames", installations.Count);
                    return;
                }
            }

            _selectedGameExecutable = game is not null && File.Exists(game.ExecutablePath)
                ? game.ExecutablePath
                : null;
            LaunchGameButton.IsEnabled = _selectedGameExecutable is not null;
            if (_loadedSave is null) SetDisplayedSavePath(save.Path);
            if (game is null && !save.Exists)
            {
                SetStatus("NoGameOrSave");
                return;
            }

            if (save.Exists)
            {
                if (!await LoadSaveFileAsync(save.Path))
                    return;
                if (CanEditLoadedSave) SetStatus(() => LocalizationService.Format(
                    "LoadedDefault",
                    FormatDetectedGame(game),
                    LocalizationService.Get("SaveFound")));
            }
            else
            {
                SetStatus(() => LocalizationService.Format(
                    "OpenManual",
                    FormatDetectedGame(game),
                    LocalizationService.Get("SaveMissing")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetStatus("DetectionLimited", exception.Message);
        }
    }

    private static string FormatDetectedGame(GameInstallation? game) => game is null
        ? LocalizationService.Get("GameMissing")
        : File.Exists(game.ExecutablePath)
            ? LocalizationService.Format("GameFound", game.ExecutablePath)
            : LocalizationService.Format("GameFoundLibrary", game.GameDirectory);

    private void SelectGameButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("SelectGameDialogTitle"),
            Filter = LocalizationService.Get("GameFilter"),
            CheckFileExists = true,
            FileName = "CutTheRopeApp.exe"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        if (!string.Equals(Path.GetFileName(dialog.FileName), "CutTheRopeApp.exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, LocalizationService.Get("InvalidGameExecutable"), LocalizationService.Get("InvalidGameExecutableTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedGameExecutable = dialog.FileName;
        LaunchGameButton.IsEnabled = true;
        SetStatus("GameSelected", dialog.FileName);
    }

    private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedGameExecutable) || !File.Exists(_selectedGameExecutable))
        {
            LaunchGameButton.IsEnabled = false;
            SetStatus("GameLaunchUnavailable");
            return;
        }

        if (IsCutTheRopeRunning())
        {
            SetStatus("GameAlreadyRunning");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/223280") { UseShellExecute = true });
            SetStatus("GameLaunchRequested");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("GameLaunchFailed", exception.Message);
        }
    }

    private static bool IsCutTheRopeRunning() => GameProcessGuard.IsRunning();

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("OpenDialogTitle"),
            Filter = LocalizationService.Get("SaveFilter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        if (!ConfirmDiscardEdits())
            return;
        if (await LoadSaveFileAsync(dialog.FileName))
            LocalizationService.SetSaveOverride(dialog.FileName);
    }

    private async void RestoreOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedPath is null || _openBackupPath is null || !CanEditLoadedSave || !File.Exists(_openBackupPath))
            return;

        if (MessageBox.Show(this, LocalizationService.Get("RestoreConfirm"), LocalizationService.Get("RestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        if (!ConfirmDiscardEdits())
            return;
        try
        {
            var restored = await RunOperationAsync(() => { SaveWriteGuard.EnsureGameClosed(); return _backupService.Restore(_openBackupPath, _loadedPath, expectedTarget: _diskVersion); });
            if (await LoadSaveFileAsync(_loadedPath)) SetStatus("Restored");
            if (restored.RetainedTransactionPaths is { Count: > 0 })
                SetStatus("SavedWithRecoveryFiles", string.Join(Environment.NewLine, restored.RetainedTransactionPaths));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or UnsupportedSaveException)
        {
            MessageBox.Show(this, DescribeSaveFailure(exception), LocalizationService.Get("RestoreFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DetectRtssButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var installations = await RunOperationAsync(() => _rtssDetector.FindInstallations());
            if (installations.Count == 0)
            {
                _rtssInstallation = null;
                SetRtssActionsEnabled(false);
                SetRtssStatus("NoRtss");
                SetRtssProfileStatus("RtssProfileNotRead");
                RefreshRtssCompatibility();
                return;
            }

            var selected = installations[0];
            _rtssInstallation = selected;
            _rtssBackupPath = null;
            _rtssProfileWasCreated = false;
            var persisted = _rtssStateStore.Load();
            if (persisted is not null && string.Equals(persisted.InstallPath, selected.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                _rtssBackupPath = persisted.BackupPath;
                _rtssProfileWasCreated = !persisted.HadOriginalProfile;
                SetRtssProfileStatus(() => LocalizationService.Format(
                    "PersistedRtssDetailed",
                    persisted.AppliedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    _rtssProfileWasCreated
                        ? LocalizationService.Get("CreatedProfileUndo")
                        : LocalizationService.Format("BackupCreated", persisted.BackupPath ?? "—")));
            }
            else if (_rtssStateStore.StateFileExists)
            {
                SetRtssProfileStatus("RtssStateInvalid");
            }
            else
            {
                SetRtssProfileStatus("RtssProfileNotRead");
            }
            SetRtssActionsEnabled(selected.HasHooks && selected.Compatibility.CanUseProfileApi);
            RefreshRtssCompatibility();
            UpdateRefreshRateAdvice();
            SetRtssStatus(() => FormatRtssInstallation(selected));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetRtssStatus("RtssLimited", exception.Message);
        }
    }

    private static string FormatRtssInstallation(RtssInstallation installation)
    {
        var files = installation.HasExecutable && installation.HasHooks
            ? LocalizationService.Get("RtssFilesOk")
            : LocalizationService.Get("RtssIncomplete");
        var running = installation.IsRunning
            ? LocalizationService.Get("RtssRunning")
            : LocalizationService.Get("RtssNotRunning");
        return LocalizationService.Format("RtssStatus", files, installation.Version ?? "—", running, installation.InstallPath);
    }

    private void GetRtssButton_Click(object sender, RoutedEventArgs e)
    {
        new RtssAcquisitionWindow(_rtssInstallation, GetPrimaryRefreshRate, RtssDistributionCatalog.Package) { Owner = this }.ShowDialog();
    }

    private void RefreshRtssCompatibility()
    {
        RtssCompatibilityText.Visibility = _rtssInstallation is null ? Visibility.Collapsed : Visibility.Visible;
        RtssCompatibilityText.Text = FormatRtssCompatibility(_rtssInstallation?.Compatibility);
    }

    internal static string FormatRtssCompatibility(RtssCompatibilityResult? result) => LocalizationService.Get(result?.Kind switch
    {
        RtssCompatibilityKind.ProfileApiVerified => "RtssApiVerified",
        RtssCompatibilityKind.KnownGood => "RtssKnownGood",
        RtssCompatibilityKind.UnknownVersion => "RtssUnknownVersion",
        RtssCompatibilityKind.UnverifiedFiles => "RtssUnverifiedFiles",
        _ => "RtssNotVerified"
    });

    private void ReadRtssButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rtssInstallation is null)
            return;

        try
        {
            var state = _rtssProfileService.Read(_rtssInstallation);
            SetRtssProfileStatus(() => FormatRtssProfile(state));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            SetRtssProfileStatus("RtssReadFailed", exception.Message);
        }
    }

    private async void ApplyRtssButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rtssInstallation is null)
            return;

        var installation = _rtssInstallation;
        var refreshRate = GetPrimaryRefreshRate();
        if (!installation.Compatibility.CanUseProfileApi || !RefreshRatePolicy.CanApplyLimit(refreshRate, installation.HasHooks) || _rtssStateStore.StateFileExists)
        {
            UpdateRefreshRateAdvice();
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            LocalizationService.Get("ApplyRtssConfirm"),
            LocalizationService.Get("ApplyRtssTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SetRtssBusy(true);
        try
        {
            var result = await _rtssElevationBroker.RunAsync(RtssHelperOperation.Apply, installation);
            if (result == RtssHelperExitCode.UserCanceled)
            {
                SetRtssProfileStatus("RtssElevationCanceled");
                return;
            }
            if (result is not RtssHelperExitCode.Success and not RtssHelperExitCode.AppliedButRtssNotRunning)
            {
                RestorePersistedRtssState();
                SetRtssProfileStatus(() => LocalizationService.Format("ApplyRtssFailed", DescribeRtssHelperExitCode(result)));
                return;
            }

            var readBack = _rtssProfileService.Read(installation);
            var persisted = _rtssStateStore.Load()
                ?? throw new InvalidDataException(LocalizationService.Get("RtssStateMissingAfterApply"));
            if (readBack.FramerateLimit != RtssProfileService.TargetFps ||
                readBack.FramerateLimitDenominator != 1 ||
                string.IsNullOrWhiteSpace(readBack.ProfilePath) ||
                !string.Equals(persisted.InstallPath, installation.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(LocalizationService.Get("RtssReadBackMismatch"));
            }

            var state = readBack with
            {
                HadOriginalProfile = persisted.HadOriginalProfile,
                BackupPath = persisted.BackupPath
            };
            _rtssBackupPath = persisted.BackupPath;
            _rtssProfileWasCreated = !persisted.HadOriginalProfile;
            var profileWasCreated = _rtssProfileWasCreated;
            SetRtssProfileStatus(() =>
            {
                var lines = new List<string>
                {
                    FormatRtssProfile(state),
                    state.BackupPath is null
                        ? LocalizationService.Get("NoOriginalBackup")
                        : LocalizationService.Format("BackupCreated", state.BackupPath)
                };
                if (profileWasCreated)
                    lines.Add(LocalizationService.Get("CreatedProfileUndo"));
                lines.Add(LocalizationService.Get(
                    result == RtssHelperExitCode.Success ? "RtssAppliedAndRunning" : "RtssAppliedNotRunning"));
                return string.Join(Environment.NewLine, lines);
            });
            UndoRtssButton.IsEnabled = _rtssBackupPath is not null || _rtssProfileWasCreated;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or System.ComponentModel.Win32Exception)
        {
            SetRtssProfileStatus("ApplyRtssFailed", exception.Message);
        }
        finally
        {
            SetRtssBusy(false);
        }
    }

    private async void UndoRtssButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rtssInstallation is null || (_rtssBackupPath is null && !_rtssProfileWasCreated))
            return;

        if (MessageBox.Show(this, LocalizationService.Get("UndoRtssConfirm"), LocalizationService.Get("UndoRtssTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var installation = _rtssInstallation;
        var profileWasCreated = _rtssProfileWasCreated;
        SetRtssBusy(true);
        try
        {
            var result = await _rtssElevationBroker.RunAsync(RtssHelperOperation.Undo, installation);
            if (result == RtssHelperExitCode.UserCanceled)
            {
                SetRtssProfileStatus("RtssElevationCanceled");
                return;
            }
            if (result != RtssHelperExitCode.Success)
            {
                RestorePersistedRtssState();
                SetRtssProfileStatus(() => LocalizationService.Format("UndoRtssFailed", DescribeRtssHelperExitCode(result)));
                return;
            }

            var state = _rtssProfileService.Read(installation);
            if (profileWasCreated && state.ProfilePath is not null)
                throw new IOException(LocalizationService.Get("RtssUndoReadBackMismatch"));

            SetRtssProfileStatus(() => profileWasCreated
                ? LocalizationService.Get("RtssCreatedRemoved")
                : LocalizationService.Format("RtssRestored", FormatRtssProfile(state)));
            _rtssBackupPath = null;
            _rtssProfileWasCreated = false;
            UndoRtssButton.IsEnabled = false;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or System.ComponentModel.Win32Exception)
        {
            SetRtssProfileStatus("UndoRtssFailed", exception.Message);
        }
        finally
        {
            SetRtssBusy(false);
        }
    }

    private void SetRtssActionsEnabled(bool enabled)
    {
        enabled = enabled && _rtssInstallation?.Compatibility.CanUseProfileApi == true;
        ReadRtssButton.IsEnabled = enabled;
        ApplyRtssButton.IsEnabled = enabled &&
            !_rtssStateStore.StateFileExists &&
            RefreshRatePolicy.CanApplyLimit(GetPrimaryRefreshRate(), _rtssInstallation?.HasHooks == true);
        UndoRtssButton.IsEnabled = enabled && (_rtssBackupPath is not null || _rtssProfileWasCreated);
    }

    private void SetRtssBusy(bool busy)
    {
        DetectRtssButton.IsEnabled = !busy;
        GetRtssButton.IsEnabled = !busy;
        if (busy)
        {
            ReadRtssButton.IsEnabled = false;
            ApplyRtssButton.IsEnabled = false;
            UndoRtssButton.IsEnabled = false;
            return;
        }

        SetRtssActionsEnabled(_rtssInstallation?.HasHooks == true);
        UpdateRefreshRateAdvice();
    }

    private void UpdateRefreshRateAdvice()
    {
        var refreshRate = GetPrimaryRefreshRate();
        if (refreshRate == 60)
        {
            SetRtssStatus("NoNeed60");
            ApplyRtssButton.IsEnabled = false;
        }
        else if (refreshRate > 0)
        {
            SetRtssStatus("Above60", refreshRate);
            ApplyRtssButton.IsEnabled = _rtssInstallation?.Compatibility.CanUseProfileApi == true && !_rtssStateStore.StateFileExists &&
                RefreshRatePolicy.CanApplyLimit(refreshRate, _rtssInstallation?.HasHooks == true);
        }
        else
        {
            SetRtssStatus("ScreenUnknown");
            ApplyRtssButton.IsEnabled = false;
        }
    }

    private static int GetPrimaryRefreshRate()
    {
        var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettings(null, EnumCurrentSettings, ref mode) ? mode.DisplayFrequency : 0;
    }

    private const int EnumCurrentSettings = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)] public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public short Orientation;
        public short PaperSize;
        public short PaperLength;
        public short PaperWidth;
        public short Scale;
        public short Copies;
        public short DefaultSource;
        public short PrintQuality;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)] public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }

    private void RestorePersistedRtssState()
    {
        var persisted = _rtssStateStore.Load();
        if (persisted is null)
        {
            if (_rtssStateStore.StateFileExists)
                SetRtssProfileStatus("RtssStateInvalid");
            else
                SetRtssProfileStatus("RtssProfileNotRead");
            return;
        }

        _rtssBackupPath = persisted.BackupPath;
        _rtssProfileWasCreated = !persisted.HadOriginalProfile;
        SetRtssProfileStatus(() => LocalizationService.Format(
            "PersistedRtssDetailed",
            persisted.AppliedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            _rtssProfileWasCreated
                ? LocalizationService.Get("CreatedProfileUndo")
                : LocalizationService.Format("BackupCreated", persisted.BackupPath ?? "—")));
    }

    private static string DescribeRtssHelperExitCode(RtssHelperExitCode exitCode) =>
        LocalizationService.Get(exitCode switch
        {
            RtssHelperExitCode.ElevationRequired => "RtssHelperElevationRequired",
            RtssHelperExitCode.GameRunning => "RtssHelperGameRunning",
            RtssHelperExitCode.InstallationNotFound => "RtssHelperInstallationMissing",
            RtssHelperExitCode.ActiveRepairExists => "RtssHelperActiveRepair",
            RtssHelperExitCode.RepairStateMissing => "RtssHelperStateMissing",
            RtssHelperExitCode.RepairStateInvalid => "RtssHelperStateInvalid",
            RtssHelperExitCode.AccessDenied => "RtssHelperAccessDenied",
            RtssHelperExitCode.RtssUnavailable => "RtssHelperUnavailable",
            RtssHelperExitCode.OperationBusy => "RtssHelperBusy",
            RtssHelperExitCode.UnverifiedRtss => "RtssUnverifiedFiles",
            _ => "RtssHelperFailed"
        });

    private static string FormatRtssProfile(RtssProfileState state) =>
        LocalizationService.Format(
            "ProfileFormat",
            state.ProfileName,
            state.FramerateLimit?.ToString() ?? "—",
            state.FramerateLimitDenominator?.ToString() ?? "—");

    private async Task<bool> LoadSaveFileAsync(string path)
    {
        try
        {
            var loaded = await RunOperationAsync(() =>
            {
                if (_backupService.InspectInterruptedSave().State != InterruptedSaveState.None)
                    throw new IOException(LocalizationService.Get("SaveRecoveryPending"));
                var snapshot = _backupService.CreateSnapshot(path, "open", deduplicateByHash: true, note: "Snapshot created when the save was opened.")
                    ?? throw new FileNotFoundException("The selected save disappeared.", path);
                var bytes = BoundedSaveFile.Read(snapshot.Path);
                var disk = SaveTargetState.FromBytes(path, bytes);
                disk.EnsureCurrent(path);
                using var input = new MemoryStream(bytes, writable: false);
                var parsed = _codec.Read(input);
                return (Save: parsed, Disk: disk, Snapshot: snapshot, Protection: _backupService.ProtectSnapshot(snapshot.Path));
            });
            var (save, diskVersion, openSnapshot, protection) = loaded;
            var validation = _validator.Validate(save);
            try { AcceptLoadedSave(save, path, openSnapshot.Path, diskVersion); }
            catch { protection?.Dispose(); throw; }
            _openBackupLease?.Dispose();
            _openBackupLease = protection;
            if (validation.CanWrite)
                SetStatus("SaveLoaded");
            else
                SetStatus("SaveReadOnlyHelp");
            await MaintainBackupRetentionAsync();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UnsupportedSaveException)
        {
            MessageBox.Show(this, DescribeSaveFailure(exception), LocalizationService.Get("OpenErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("OpenFailed");
            return false;
        }
    }

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedSave is null || _loadedPath is null || !CanEditLoadedSave || !TryCommitGridEdits())
            return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("SaveAsTitle"),
            FileName = Path.GetFileName(_loadedPath),
            Filter = LocalizationService.Get("SaveFilter"),
            AddExtension = false,
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var expectedTarget = ReviewSaveDestination(dialog.FileName, path =>
                MessageBox.Show(this, LocalizationService.Format("OverwriteReviewedTarget", path),
                    LocalizationService.Get("SaveAsTitle"), MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
            if (expectedTarget is null) return;
            var editedSave = BuildEditedSave(out var changedCount);
            var backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CutTheSave",
                "backups");
            var snapshot = await RunOperationAsync(() =>
            {
                SaveWriteGuard.EnsureGameClosed();
                return new AtomicSaveService(_codec, _validator).Save(dialog.FileName, editedSave, backupDirectory, expectedTarget: expectedTarget);
            });
            AcceptSavedDraft(editedSave, dialog.FileName, new SaveTargetState(snapshot.Path, true, snapshot.Length, snapshot.Sha256));
            SetDisplayedSavePath(dialog.FileName);
            SetStatus("SafeSaved", snapshot.Path, snapshot.Sha256, changedCount);
            if (snapshot.RetainedPaths is { Count: > 0 })
                SetStatus("SavedWithRecoveryFiles", string.Join(Environment.NewLine, snapshot.RetainedPaths));
            await MaintainBackupRetentionAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UnsupportedSaveException or FormatException or OverflowException or InvalidOperationException)
        {
            MessageBox.Show(this, DescribeSaveFailure(exception), LocalizationService.Get("SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("SaveFailed");
        }
    }

    private void UnlockAllButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatch(
            "UnlockAll",
            "UnlockConfirm",
            save => _levelProgress.SetAllUnlocked(save));
    }

    private void StarsAllButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatch(
            "StarsAll",
            "StarsConfirm",
            save => _levelProgress.SetAllStars(save));
    }

    private void ScoresAllButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatch(
            "ScoresAll",
            "ScoresConfirm",
            save => _levelProgress.SetAllScores(save));
    }

    private void CompleteAllButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatch(
            "CompleteAll",
            "CompleteConfirm",
            save => _levelProgress.SetAllCompleted(save));
    }

    private void UndoMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_openedSaveSnapshot is null)
            return;

        if (MessageBox.Show(this, LocalizationService.Get("UndoMemoryConfirm"), LocalizationService.Get("UndoMemoryTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        CancelPendingGridEdits();
        _loadedSave = CloneSave(_openedSaveSnapshot);
        RefreshEntryGrid();
        SetStatus("UndoMemoryDone");
    }

    private void ApplyBatch(string titleKey, string messageKey, Func<CutTheRopeSave, int> operation)
    {
        if (_loadedSave is null)
            return;

        var title = LocalizationService.Get(titleKey);
        var message = LocalizationService.Get(messageKey);
        if (MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var changed = ApplyBatchToDraft(operation);
        if (changed is null)
            return;
        SetStatus(() => LocalizationService.Format("BatchApplied", LocalizationService.Get(titleKey), changed));
    }

    private void RefreshEntryGrid()
    {
        if (_loadedSave is null)
            return;

        EntriesGrid.ItemsSource = BuildRows(_loadedSave);
        LevelsGrid.ItemsSource = BuildLevelRows(_loadedSave);
        var validation = _validator.Validate(_loadedSave);
        RenderValidation(validation);
        SaveAsButton.IsEnabled = CanEditLoadedSave && validation.CanWrite;
        SetEditingEnabled(CanEditLoadedSave && validation.CanWrite);
        RefreshDirtyIndicator();
    }

    private void SetEditingEnabled(bool enabled)
    {
        EntriesGrid.IsReadOnly = LevelsGrid.IsReadOnly = !enabled;
        UnlockAllButton.IsEnabled = enabled;
        StarsAllButton.IsEnabled = enabled;
        ScoresAllButton.IsEnabled = enabled;
        CompleteAllButton.IsEnabled = enabled;
        UndoMemoryButton.IsEnabled = enabled && _openedSaveSnapshot is not null;
    }

    private static CutTheRopeSave CloneSave(CutTheRopeSave source)
    {
        var clone = new CutTheRopeSave();
        clone.IntEntries.AddRange(source.IntEntries);
        clone.StringEntries.AddRange(source.StringEntries);
        return clone;
    }

    internal CutTheRopeSave BuildEditedSave(out int changedCount)
    {
        if (_loadedSave is null)
            throw new InvalidOperationException("No save is loaded.");

        // Accepted cell edits already update the shared model. Re-parsing display text
        // here would overwrite level edits and reinterpret untouched literal backslashes.
        var edited = CloneSave(_loadedSave);
        changedCount = _savedSaveSnapshot is null ? 0
            : edited.IntEntries.Count(entry => !_savedSaveSnapshot.IntEntries.Contains(entry))
                + edited.StringEntries.Count(entry => !_savedSaveSnapshot.StringEntries.Contains(entry));
        return edited;
    }

    private static ObservableCollection<SaveEntryRow> BuildRows(CutTheRopeSave save)
    {
        var rows = new ObservableCollection<SaveEntryRow>();
        for (var index = 0; index < save.IntEntries.Count; index++)
        {
            var entry = save.IntEntries[index];
            rows.Add(new SaveEntryRow("Int32", entry.Key, entry.Value.ToString(CultureInfo.InvariantCulture), index));
        }

        for (var index = 0; index < save.StringEntries.Count; index++)
        {
            var entry = save.StringEntries[index];
            rows.Add(new SaveEntryRow("String", entry.Key, entry.Value.Replace("\0", "\\0", StringComparison.Ordinal), index));
        }

        return rows;
    }

    private ObservableCollection<LevelRow> BuildLevelRows(CutTheRopeSave save)
    {
        var rows = new ObservableCollection<LevelRow>();
        foreach (var level in new LevelProgressService().Project(save))
            rows.Add(new LevelRow(level, _savedSaveSnapshot));
        return rows;
    }

    internal void EntriesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not SaveEntryRow row)
            return;
        e.Cancel = !TryApplyRawEdit(row, (e.EditingElement as TextBox)?.Text ?? row.Value);
    }

    internal void LevelsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not LevelRow row)
            return;
        e.Cancel = !TryApplyLevelEdit(row, LevelsGrid.Columns.IndexOf(e.Column), (e.EditingElement as TextBox)?.Text ?? string.Empty);
    }

    internal sealed class SaveEntryRow : INotifyPropertyChanged
    {
        public SaveEntryRow(string kind, string key, string value, int sourceIndex)
        {
            Kind = kind;
            Key = key;
            Value = value;
            SourceIndex = sourceIndex;
        }

        public string Kind { get; }
        public string Key { get; }
        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public int SourceIndex { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    internal sealed class LevelRow : INotifyPropertyChanged
    {
        private readonly int _originalUnlocked;
        private readonly int _originalStars;
        private readonly int _originalScore;

        public LevelRow(LevelProgress level, CutTheRopeSave? baseline)
        {
            BoxIndex = level.BoxIndex;
            LevelIndex = level.LevelIndex;
            Unlocked = level.Unlocked;
            Stars = level.Stars;
            Score = level.Score;
            _originalUnlocked = baseline?.TryGetInt(UnlockedKey, out var unlocked) == true ? unlocked : Unlocked;
            _originalStars = baseline?.TryGetInt(StarsKey, out var stars) == true ? stars : Stars;
            _originalScore = baseline?.TryGetInt(ScoreKey, out var score) == true ? score : Score;
        }

        public int BoxIndex { get; }
        public int LevelIndex { get; }
        public int BoxNumber => BoxIndex + 1;
        public int LevelNumber => LevelIndex + 1;
        public int Unlocked { get; private set; }
        public int Stars { get; private set; }
        public int Score { get; private set; }
        public string Changed => Unlocked != _originalUnlocked || Stars != _originalStars || Score != _originalScore
            ? LocalizationService.Get("Yes") : LocalizationService.Get("No");
        public void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Changed)));
        public string UnlockedKey => $"UNLOCKED_{BoxIndex}_{LevelIndex}";
        public string StarsKey => $"STARS_{BoxIndex}_{LevelIndex}";
        public string ScoreKey => $"SCORE_{BoxIndex}_{LevelIndex}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetValue(int displayIndex, int value)
        {
            if (displayIndex == 2) Unlocked = value;
            if (displayIndex == 3) Stars = value;
            if (displayIndex == 4) Score = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
