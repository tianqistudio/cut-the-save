using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using CutTheSave.Core;
using CutTheSave.Infrastructure.Windows;

namespace CutTheSave.App;

internal static class RtssDistributionCatalog
{
    // Deliberately unset: official page returned 403 during package review.
    // Fill ONLY after official URL, exact ZIP/EXE size and both hashes are verified.
    // Never load an unsigned override from a user-writable INI or arbitrary URL.
    internal static RtssPackageManifest? Package => null;
}

public partial class RtssAcquisitionWindow : Window
{
    private readonly RtssPackageManifest? _manifest;
    private readonly Func<int> _refreshRate;
    private readonly string _cacheRoot;
    private readonly Func<RtssPackageDownloader> _downloader;
    private CancellationTokenSource? _cancellation;
    private VerifiedRtssPackage? _verified;

    internal RtssAcquisitionWindow(RtssInstallation? installation, Func<int> refreshRate,
        RtssPackageManifest? manifest, string? cacheRoot = null, Func<RtssPackageDownloader>? downloader = null)
    {
        InitializeComponent();
        manifest?.Validate();
        _manifest = manifest;
        _refreshRate = refreshRate;
        _cacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CutTheSave", "downloads");
        _downloader = downloader ?? (() => new RtssPackageDownloader());
        Title = TitleText.Text = LocalizationService.Get("RtssSetupTitle");
        HelpText.Text = LocalizationService.Get("RtssSetupHelp");
        DisplayText.Text = LocalizationService.Get(refreshRate() == 60 ? "NoNeed60" : "RtssSetupDisplay");
        CompatibilityText.Text = MainWindow.FormatRtssCompatibility(installation?.Compatibility);
        PackageText.Text = manifest is null ? LocalizationService.Get("RtssPackageUnavailable") :
            LocalizationService.Format("RtssPackageDetails", manifest.Version, manifest.FileName, manifest.Size, manifest.Sha256);
        StepsText.Text = LocalizationService.Get("RtssSetupSteps");
        foreach (var item in new[] { (OfficialButton, "RtssOfficialPage", "TipRtssOfficialPage"),
            (DownloadButton, "RtssDownload", "TipRtssDownload"), (InstallButton, "RtssInstall", "TipRtssInstall"),
            (CancelDownloadButton, "Cancel", "Cancel"), (CloseButton, "Close", "Close") })
        {
            item.Item1.Content = LocalizationService.Get(item.Item2);
            item.Item1.ToolTip = LocalizationService.Get(item.Item3);
            AutomationProperties.SetName(item.Item1, LocalizationService.Get(item.Item2));
        }
        Closing += (_, _) => _cancellation?.Cancel();
        RefreshActions();
    }

    private void RefreshActions()
    {
        DownloadButton.IsEnabled = _manifest is not null && _cancellation is null && _refreshRate() > 60 && _verified is null;
        InstallButton.IsEnabled = _verified is not null && _cancellation is null && _refreshRate() > 60;
        CancelDownloadButton.Visibility = _cancellation is null ? Visibility.Collapsed : Visibility.Visible;
        DownloadProgress.Visibility = _cancellation is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e) => await DownloadAsync(() => Confirm("RtssDownloadConfirm"));

    internal async Task DownloadAsync(Func<bool> confirm)
    {
        if (_manifest is null || _cancellation is not null || _verified is not null || _refreshRate() <= 60 || !confirm()) return;
        _cancellation = new CancellationTokenSource();
        RefreshActions();
        StatusText.Text = LocalizationService.Get("RtssDownloading");
        try
        {
            using var downloader = _downloader();
            _verified = await downloader.DownloadAsync(_manifest, _cacheRoot, new Progress<double>(value => DownloadProgress.Value = value), _cancellation.Token);
            StatusText.Text = LocalizationService.Get("RtssDownloadReady");
            StatusText.ToolTip = _verified.InstallerPath;
        }
        catch (OperationCanceledException) { StatusText.Text = LocalizationService.Get(_cancellation.IsCancellationRequested ? "RtssDownloadCanceled" : "RtssDownloadFailed"); }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException or FormatException)
        {
            StatusText.Text = LocalizationService.Get("RtssDownloadFailed");
            StatusText.ToolTip = exception is RtssPackageCleanupException cleanup ? string.Join(Environment.NewLine, cleanup.RetainedPaths) : exception.Message;
            if (exception is RtssPackageCleanupException) StatusText.Text += " " + LocalizationService.Get("RtssUnverifiedRetained");
        }
        finally { _cancellation.Dispose(); _cancellation = null; RefreshActions(); }
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e) => StartInstaller(() => Confirm("RtssInstallConfirm"));

    internal void StartInstaller(Func<bool> confirm, Action<string>? launch = null, Action? ensureGameClosed = null)
    {
        if (_verified is null || _refreshRate() <= 60 || !confirm()) return;
        try
        {
            (ensureGameClosed ?? (() => SaveWriteGuard.EnsureGameClosed()))();
            using var lease = _verified.OpenVerifiedInstaller();
            if (launch is null)
                Process.Start(new ProcessStartInfo(_verified.InstallerPath) { UseShellExecute = true });
            else launch(_verified.InstallerPath);
            StatusText.Text = LocalizationService.Get("RtssInstallerStarted");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _verified = null;
            RefreshActions();
            StatusText.Text = LocalizationService.Get("RtssInstallFailed");
            StatusText.ToolTip = exception.Message;
        }
    }

    private bool Confirm(string key) => MessageBox.Show(this, LocalizationService.Get(key), Title,
        MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void OfficialButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(RtssPackageManifest.OfficialPage) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        { StatusText.Text = LocalizationService.Get("BrowserFailed"); StatusText.ToolTip = RtssPackageManifest.OfficialPage; }
    }
}
