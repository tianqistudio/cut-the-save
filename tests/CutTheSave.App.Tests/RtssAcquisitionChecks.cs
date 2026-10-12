using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CutTheSave.App;
using CutTheSave.Core;
using CutTheSave.Infrastructure.Windows;

internal static class RtssAcquisitionChecks
{
    internal static void Run(string? previewDirectory = null)
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-rtss-guide-ui-");
        var oldContext = SynchronizationContext.Current;
        var oldLanguage = LocalizationService.Current;
        var oldTheme = AppearanceService.CurrentTheme;
        var oldMode = AppearanceService.SelectedColorMode;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var payload = Encoding.ASCII.GetBytes("Synthetic UI download; never execute");
            var hash = Convert.ToHexString(SHA256.HashData(payload));
            var metadata = new RtssPackageManifest(1, "7.3.5.28314", "RTSS-test.exe", payload.Length, hash,
                "https://www.guru3d.com/synthetic-test-only", "RTSS-test.exe", payload.Length, hash);
            Check(RtssDistributionCatalog.Package is null, "no unverified production download entry");
            if (previewDirectory is not null) Directory.CreateDirectory(previewDirectory);
            foreach (var language in Enum.GetValues<AppLanguage>())
            foreach (var theme in Enum.GetValues<AppVisualTheme>())
            foreach (var mode in new[] { AppColorMode.Light, AppColorMode.Dark })
            foreach (var state in new[] { "missing", "unknown", "reviewed-metadata-fixture", "60hz" })
            {
                IniLanguageChecks.Select(language, Path.Combine(root.FullName, "CutTheSave.ini"));
                AppearanceService.ApplyForSession(theme, mode);
                var installation = state == "missing" ? null : new RtssInstallation(@"C:\Example\RTSS", "7.3.5.28314", true, true, false, null, null)
                { Compatibility = new(state == "unknown" ? RtssCompatibilityKind.UnknownVersion : RtssCompatibilityKind.ProfileApiVerified, "7.3.5.28314") };
                var window = new RtssAcquisitionWindow(installation, () => state == "60hz" ? 60 : 144,
                    state is "reviewed-metadata-fixture" or "60hz" ? metadata : null, Path.Combine(root.FullName, "unused-cache"));
                Check(window.Title == LocalizationService.Get("RtssSetupTitle"), "guide follows INI language");
                Check(window.DownloadButton.IsEnabled == (state == "reviewed-metadata-fixture") && !window.InstallButton.IsEnabled, "metadata and 60Hz gating");
                var confirmed = 0;
                Pump(window.DownloadAsync(() => { confirmed++; return false; }));
                Check(confirmed == (state == "reviewed-metadata-fixture" ? 1 : 0), "unavailable download never asks a misleading confirmation");
                window.StartInstaller(() => throw new Exception("installer enabled before download"));
                foreach (var button in Buttons(window)) Check(button.ToolTip is not null && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), "tooltips and accessible names");
                var name = $"{theme}-{mode}-{LocalizationService.ToCode(language)}-{state}";
                var target = previewDirectory ?? root.FullName;
                AppearancePreview.Render(window, 804, 551, Path.Combine(target, name + ".png"));
                AssertFooter(window);
                AppearancePreview.Render(window, 624, 431, Path.Combine(target, name + "-minimum.png"));
                AssertFooter(window);
                window.Close();
            }
            Check(!Directory.Exists(Path.Combine(root.FullName, "unused-cache")), "cancel and 60Hz do not create a download cache");
            LocalizationService.UseLanguageForSession(AppLanguage.EnUs);
            var rate = 144;
            var cache = Path.Combine(root.FullName, "download");
            var downloaded = new RtssAcquisitionWindow(null, () => rate, metadata, cache,
                () => new RtssPackageDownloader(new Transport(payload)));
            Pump(downloaded.DownloadAsync(() => true));
            Check(downloaded.InstallButton.IsEnabled && !downloaded.DownloadButton.IsEnabled, "verified download offers separate install action");
            var launches = 0;
            Action<string> launch = path =>
            {
                launches++;
                var denied = false;
                try { File.WriteAllBytes(path, payload); } catch (IOException) { denied = true; }
                Check(denied, "verified file lease remains locked through launch");
            };
            downloaded.StartInstaller(() => false, launch, () => { });
            Check(launches == 0, "installer cancellation never launches");
            downloaded.StartInstaller(() => true, launch, () => { });
            Check(launches == 1 && downloaded.StatusText.Text == LocalizationService.Get("RtssInstallerStarted"), "launch request not reported as completed installation");
            rate = 60;
            downloaded.StartInstaller(() => throw new Exception("60Hz requested installer confirmation"), launch, () => { });
            Check(launches == 1, "60Hz rechecked at launch");
            rate = 144;
            var path = Directory.GetFiles(cache, "*.exe", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(path, new byte[payload.Length]);
            downloaded.StartInstaller(() => true, launch, () => { });
            Check(launches == 1 && !downloaded.InstallButton.IsEnabled, "tampered download rejected before launch");
            downloaded.Close();

            var canceled = new RtssAcquisitionWindow(null, () => 144, metadata, Path.Combine(root.FullName, "cancel"),
                () => new RtssPackageDownloader(new Transport(null)));
            var pending = canceled.DownloadAsync(() => true);
            Check(canceled.CancelDownloadButton.Visibility == Visibility.Visible, "cancel offered during download");
            canceled.CancelDownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(pending);
            Check(!canceled.InstallButton.IsEnabled && canceled.StatusText.Text == LocalizationService.Get("RtssDownloadCanceled"), "UI cancellation cannot enable installation");
            canceled.Close();
            Console.WriteLine("PASS 96 INI-driven RTSS guide layouts, unavailable/60Hz gates, separate confirmations, cancellation, and changed-installer rejection");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
            LocalizationService.UseLanguageForSession(oldLanguage);
            AppearanceService.ApplyForSession(oldTheme, oldMode);
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true);
        }
    }

    private static IEnumerable<Button> Buttons(RtssAcquisitionWindow window) =>
        [window.OfficialButton, window.DownloadButton, window.InstallButton, window.CloseButton];
    private static void AssertFooter(RtssAcquisitionWindow window)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var button in Buttons(window))
        {
            var point = button.TranslatePoint(new Point(), root);
            Check(point.X >= -1 && point.Y >= -1 && point.X + button.ActualWidth <= root.ActualWidth + 1 &&
                point.Y + button.ActualHeight <= root.ActualHeight + 1, "guide footer fits minimum layout");
        }
    }
    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new Exception("RTSS UI fixture timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class Transport(byte[]? payload) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (payload is null) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(payload!) };
        }
    }
}
