using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Threading;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CutTheSave.App;
using CutTheSave.Infrastructure.Windows;

if (args.Length == 2 && args[0] == "--render-appearance")
{
    AppearancePreview.Run(Path.GetFullPath(args[1]));
    return;
}

if (args.Length == 1 && args[0] == "--check-editor")
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new App(suppressStartup: true) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            LocalizationService.UseLanguageForSession(AppLanguage.EnUs);
            var main = new MainWindow(suppressStartupDetection: true);
            EditorSafetyChecks.Run(main);
            WriteSafetyChecks.Run(main);
            main.Close();
            app.Shutdown();
        }
        catch (Exception exception) { failure = exception; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
    return;
}

if (args.Length == 2 && args[0] == "--check-backups")
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new App(suppressStartup: true) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            BackupWindowChecks.Run(Path.GetFullPath(args[1]));
            SaveRecoveryWindowChecks.Run(Path.Combine(Path.GetFullPath(args[1]), "save-recovery"));
            RtssAcquisitionChecks.Run(Path.Combine(Path.GetFullPath(args[1]), "rtss-guide"));
            app.Shutdown();
        }
        catch (Exception exception) { failure = exception; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
    return;
}

var root = Path.Combine(Path.GetTempPath(), $"CutTheSave-support-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
var output = Path.Combine(root, "support.zip");
try
{
    Assert(typeof(ReadmeWindow).Assembly.GetManifestResourceNames().Any(name => name.EndsWith("guide.md", StringComparison.OrdinalIgnoreCase)), "embedded guide resource exists");
    var service = new SupportBundleService();
    service.Create(
        output,
        144,
        [new GameInstallation($"C:\\Users\\{Environment.UserName}\\SteamLibrary", $"C:\\Users\\{Environment.UserName}\\SteamLibrary\\steamapps\\common\\Cut the Rope", "C:\\Users\\demo\\CutTheRopeApp.exe")],
        [new RtssInstallation("C:\\Program Files (x86)\\RivaTuner Statistics Server", "7.3.5", true, true, false, null, null)]);

    using var archive = ZipFile.OpenRead(output);
    var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(names.Contains("app/version.txt"), "version file exists");
    Assert(names.Contains("environment.json"), "environment file exists");
    Assert(names.Contains("sanitized-settings.json"), "settings file exists");
    Assert(names.Contains("steam-summary.json"), "Steam summary exists");
    Assert(names.Contains("rtss-summary.json"), "RTSS summary exists");
    Assert(!names.Any(name => name.Contains("ctr_save", StringComparison.OrdinalIgnoreCase)), "save content is excluded");
    var steamSummary = Read(archive, "steam-summary.json");
    Assert(steamSummary.Contains("\\u003CUSER\\u003E", StringComparison.Ordinal), "user path is sanitized");
    Console.WriteLine("PASS Support bundle is sanitized and excludes save content");
    SupportBundleChecks.Run(root);
    RunSettingsPreservationTest(root);
    RunUpdateConfigurationTest();
    UpdateTrustChecks.Run(root);
    RunUpdateCheckStateTest(root);
    RunRtssHelperProtocolTest();
    RunUiSmokeTest();
}

finally
{
    if (Directory.Exists(root))
        if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(root, true);
}

static void RunSettingsPreservationTest(string root)
{
    var path = Path.Combine(root, "CutTheSave.ini");
    File.WriteAllText(path, "[General]\nLanguage=zh-CN\nSavePath=C:\\save\\ctr_save.bin\n\n[Future]\nKeep=1\n");
    AppSettingsFile.SetGeneralValues(new Dictionary<string, string?>
    {
        ["Theme"] = "Windows11",
        ["ColorMode"] = "System"
    }, path);
    Assert(AppSettingsFile.ReadGeneralValue("Language", path) == "zh-CN", "appearance settings preserve the language preference");
    Assert(AppSettingsFile.ReadGeneralValue("SavePath", path) == "C:\\save\\ctr_save.bin", "appearance settings preserve the save override");
    Assert(AppSettingsFile.ReadGeneralValue("Theme", path) == "Windows11", "theme is saved in the shared INI");
    Assert(AppSettingsFile.ReadGeneralValue("ColorMode", path) == "System", "color mode is saved in the shared INI");
    Assert(File.ReadAllText(path).Contains("[Future]" + Environment.NewLine + "Keep=1", StringComparison.Ordinal), "unknown INI sections are preserved");

    AppSettingsFile.SetGeneralValue("Language", "en-US", path);
    IniLanguageChecks.Run(root);
    Assert(AppSettingsFile.ReadGeneralValue("Theme", path) == "Windows11", "language updates preserve the theme preference");
    Assert(AppSettingsFile.ReadGeneralValue("ColorMode", path) == "System", "language updates preserve the color mode preference");
    Console.WriteLine("PASS shared INI preserves language, save, appearance, and unknown settings");
}

static void RunUpdateCheckStateTest(string root)
{
    var statePath = Path.Combine(root, "update-check.ini");
    var store = new UpdateCheckStateStore(statePath);
    var now = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
    Assert(store.ShouldCheck(now), "first startup update check is eligible");
    store.MarkSuccessful(now);
    Assert(!store.ShouldCheck(now.AddHours(23)), "successful update check is throttled for 24 hours");
    Assert(store.ShouldCheck(now.AddHours(24)), "update check resumes after 24 hours");
    File.WriteAllText(statePath, "not-a-date");
    Assert(store.ShouldCheck(now), "malformed update-check state fails open without blocking startup");
    Console.WriteLine("PASS startup update check is asynchronous-ready and throttled safely");
}

static void RunUpdateConfigurationTest()
{
    var key = Convert.ToBase64String(new byte[32]);
    var validSources = new[]
    {
        new UpdateSourceConfiguration("site", new Uri("https://updates.example/manifest.json"), new Uri("https://updates.example/manifest.sig")),
        new UpdateSourceConfiguration("github", new Uri("https://github.example/manifest.json"), new Uri("https://github.example/manifest.sig"))
    };
    var valid = new UpdateConfiguration(true, key, "stable", ["updates.example", "github.example"], validSources) { KeyId = "test-key" };
    Assert(valid.IsReady, "valid two-source HTTPS update configuration is ready");
    Assert(valid.ValidationError is null, "valid update configuration has no validation error");

    var wrongKey = valid with { PublicKeyBase64 = Convert.ToBase64String(new byte[31]) };
    Assert(!wrongKey.IsReady, "wrong-length update key is rejected");
    var httpSource = validSources[0] with { ManifestUri = new Uri("http://updates.example/manifest.json") };
    var wrongScheme = valid with { Sources = [httpSource, validSources[1]] };
    Assert(!wrongScheme.IsReady, "HTTP update source is rejected");
    var oneSource = valid with { Sources = [validSources[0]] };
    Assert(!oneSource.IsReady, "single update source is rejected");
    Console.WriteLine("PASS signed update configuration validates key, HTTPS, host, and dual-source requirements");
}

static void RunRtssHelperProtocolTest()
{
    var normal = RtssHelperProtocol.Parse([]);
    Assert(!normal.IsHelperMode, "normal app startup is not mistaken for RTSS helper mode");

    const string installPath = "C:\\Program Files (x86)\\RivaTuner Statistics Server";
    var applyArguments = RtssHelperProtocol.BuildArguments(RtssHelperOperation.Apply, installPath);
    var apply = RtssHelperProtocol.Parse(applyArguments);
    Assert(apply.IsHelperMode && apply.IsValid, "elevated RTSS apply arguments round-trip");
    Assert(apply.Operation == RtssHelperOperation.Apply && apply.InstallPath == installPath, "elevated RTSS apply preserves the exact installation path");

    var undoArguments = RtssHelperProtocol.BuildArguments(RtssHelperOperation.Undo, installPath);
    var undo = RtssHelperProtocol.Parse(undoArguments);
    Assert(undo.IsHelperMode && undo.IsValid && undo.Operation == RtssHelperOperation.Undo, "elevated RTSS undo arguments round-trip");

    var invalid = RtssHelperProtocol.Parse([RtssHelperProtocol.CommandSwitch, "delete-global", installPath]);
    Assert(invalid.IsHelperMode && !invalid.IsValid, "helper protocol rejects operations outside apply and undo");
    Console.WriteLine("PASS elevated RTSS helper protocol is narrow and leaves normal startup unchanged");
}

static string Read(ZipArchive archive, string name)
{
    using var stream = archive.GetEntry(name)!.Open();
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL {name}");
}

static void RunUiSmokeTest()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new App(suppressStartup: true);
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var originalLanguage = LocalizationService.Current;
            var originalTheme = AppearanceService.CurrentTheme;
            var originalColorMode = AppearanceService.SelectedColorMode;
            Assert(LocalizationService.RecommendForSystem(new CultureInfo("zh-CN")) == AppLanguage.ZhCn, "Simplified Chinese Windows culture recommends zh-CN");
            Assert(LocalizationService.RecommendForSystem(new CultureInfo("zh-TW")) == AppLanguage.ZhTw, "Traditional Chinese Windows culture recommends zh-TW");
            Assert(LocalizationService.RecommendForSystem(new CultureInfo("en-US")) == AppLanguage.EnUs, "English Windows culture recommends en-US");
            Assert(AppearanceService.ParseTheme(null) == AppVisualTheme.Windows11, "Windows 11 is the default theme");
            Assert(AppearanceService.ParseColorMode(null) == AppColorMode.System, "follow-system is the default color mode");
            Assert(AppearanceService.ResolveEffectiveColorMode(AppColorMode.System, true) == AppColorMode.Dark, "automatic mode follows a dark Windows setting");
            Assert(AppearanceService.ResolveEffectiveColorMode(AppColorMode.System, false) == AppColorMode.Light, "automatic mode follows a light Windows setting");
            Assert(AppearanceService.ResolveEffectiveColorMode(AppColorMode.Light, true) == AppColorMode.Light, "manual light mode overrides a dark Windows setting");
            Assert(AppearanceService.ResolveEffectiveColorMode(AppColorMode.Dark, false) == AppColorMode.Dark, "manual dark mode overrides a light Windows setting");

            AppearanceService.ApplyForSession(AppVisualTheme.Windows11, AppColorMode.System, systemDarkOverride: false);
            Assert(AppearanceService.CurrentTheme == AppVisualTheme.Windows11 && AppearanceService.EffectiveColorMode == AppColorMode.Light, "Windows 11 light resources apply for a light system");
            Assert(ResourceColor("PageBackgroundBrush") == Color.FromRgb(0xF3, 0xF3, 0xF3), "Windows 11 light palette is active");
            Assert(HasThemeDictionary("Win11.xaml"), "Windows 11 resource dictionary is active");

            LocalizationService.UseLanguageForSession(AppLanguage.EnUs);
            var picker = new LanguageSelectionWindow(AppLanguage.EnUs, AppLanguage.ZhCn, "zh-CN");
            Assert(picker.LanguageComboBox.Items.Count == 3, "language picker exposes three languages");
            Assert(picker.LanguageComboBox.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(["简体中文", "繁體中文", "English"]), "language picker labels are complete");
            Assert(picker.Title == "Choose a language", "language picker title bar follows the active UI language");
            Assert(picker.RecommendationText.Text.Contains("Recommended: Simplified Chinese (zh-CN)", StringComparison.Ordinal), "language picker distinguishes the system recommendation from the current UI language");
            picker.Close();

            var appearance = new AppearanceWindow();
            Assert(appearance.Title == "Choose appearance", "appearance picker follows the active UI language");
            Assert(appearance.ThemeComboBox.Items.Count == 2, "appearance picker exposes Windows 11 and Classic themes");
            Assert(appearance.ColorModeComboBox.Items.Count == 3, "appearance picker exposes automatic, light, and dark modes");
            Assert(appearance.ThemeComboBox.SelectedIndex == 0 && appearance.ColorModeComboBox.SelectedIndex == 0, "appearance picker reflects the default Windows 11 automatic selection");
            appearance.Close();

            AppearanceService.ApplyForSession(AppVisualTheme.Windows11, AppColorMode.Dark, systemDarkOverride: false);
            Assert(ResourceColor("PageBackgroundBrush") == Color.FromRgb(0x20, 0x20, 0x20), "manual dark mode applies the Windows 11 dark palette");
            AppearanceService.ApplyForSession(AppVisualTheme.Classic, AppColorMode.Light, systemDarkOverride: true);
            Assert(ResourceColor("PageBackgroundBrush") == Color.FromRgb(0xF6, 0xF7, 0xF9), "Classic light preserves the original page color");
            Assert(HasThemeDictionary("Classic.xaml"), "Classic resource dictionary is active");
            AppearanceService.ApplyForSession(AppVisualTheme.Windows11, AppColorMode.System, systemDarkOverride: false);

            var readme = new ReadmeWindow();
            Assert(readme.ReadmeText.Text.Contains("简体中文", StringComparison.Ordinal), "README contains Simplified Chinese");
            Assert(readme.ReadmeText.Text.Contains("繁體中文", StringComparison.Ordinal), "README contains Traditional Chinese");
            Assert(readme.ReadmeText.Text.Contains("English", StringComparison.Ordinal), "README contains English");
            readme.Close();

            var main = new MainWindow(suppressStartupDetection: true);
            Assert(main.FilePathText.Text == "No save file is open", "English initial save placeholder is localized");
            Assert(main.ValidationText.Text == "Not loaded", "English validation placeholder is localized");
            Assert(main.StatusText.Text == "Ready.", "English dynamic status is localized");
            Assert(main.RtssProfileStatusText.Text.StartsWith("Previous fix record found:", StringComparison.Ordinal) || main.RtssProfileStatusText.Text == "The Profile has not been read yet.", "English RTSS dynamic status is localized");
            Assert((string?)main.AppearanceButton.Content == "Appearance", "appearance entry point is localized");
            Assert(main.AppearanceButton.ToolTip?.ToString()?.Contains("Windows 11", StringComparison.Ordinal) == true, "appearance tooltip explains the active theme");

            LocalizationService.UseLanguageForSession(AppLanguage.ZhTw);
            main.ApplyLanguage();
            Assert(main.FilePathText.Text == "尚未開啟檔案", "Traditional Chinese initial save placeholder is localized");
            Assert(main.ValidationText.Text == "未載入", "Traditional Chinese validation placeholder is localized");
            Assert(main.StatusText.Text == "就緒。", "Traditional Chinese dynamic status is localized");

            LocalizationService.UseLanguageForSession(originalLanguage);
            main.ApplyLanguage();
            Assert(main.GuideActionButton.ToolTip is not null, "guide action has a tooltip");
            Assert(main.SelectGameButton.ToolTip is not null, "manual game picker has a tooltip");
            Assert(main.LaunchGameButton.ToolTip is not null, "Steam launch button has a tooltip");
            Assert(!main.LaunchGameButton.IsEnabled, "Steam launch button starts disabled until an executable is confirmed");
            Assert(main.BackupsButton.ToolTip is not null, "backup viewer has a tooltip");
            Assert(main.SupportButton.ToolTip is not null, "support button has a tooltip");
            Assert(main.AppearanceButton.ToolTip is not null, "appearance button has a tooltip");
            Assert(main.UnlockAllButton.ToolTip is not null, "batch action has a tooltip");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.GuideActionButton)), "guide action has an accessibility name");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.SelectGameButton)), "manual game picker has an accessibility name");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.LaunchGameButton)), "Steam launch button has an accessibility name");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.BackupsButton)), "backup viewer has an accessibility name");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.SupportButton)), "support button has an accessibility name");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.AppearanceButton)), "appearance button has an accessibility name");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(main.UnlockAllButton)), "batch action has an accessibility name");
            Assert(main.MinWidth <= 720 && main.MinHeight <= 420, "main window fits smaller logical work areas");
            main.Show();
            main.UpdateLayout();
            main.Activate();
            main.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
            AssertTabTraversal(main);
            AssertScaledLayout(main);
            UiOptimizationChecks.Run(main);
            var backups = new BackupsWindow(new CutTheSave.Core.BackupService(Path.Combine(Path.GetTempPath(), "CutTheSave-empty-backups-" + Guid.NewGuid().ToString("N"))));
            Assert(backups.TitleText.Text.Length > 0 && backups.RefreshButton.Content is not null, "backup viewer initializes its localized controls");
            backups.Show();
            backups.Close();
            BackupWindowChecks.Run();
            SaveRecoveryWindowChecks.Run();
            RtssAcquisitionChecks.Run();
            LocalizationService.UseLanguageForSession(AppLanguage.EnUs);
            EditorSafetyChecks.Run(main);
            WriteSafetyChecks.Run(main);
            main.Close();
            AppearanceService.ApplyForSession(originalTheme, originalColorMode);
            app.Shutdown();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    Console.WriteLine("PASS WPF UI smoke test covers languages, README, tooltips, accessibility names, backup viewer, keyboard traversal, and minimum layout");
}

static Color ResourceColor(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

static bool HasThemeDictionary(string fileName) => Application.Current.Resources.MergedDictionaries.Any(
    dictionary => dictionary.Source?.OriginalString.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) == true);

static void AssertTabTraversal(MainWindow main)
{
    var tabStops = FindTabStops(main).ToArray();
    var enabledStops = tabStops.Where(control => control.IsEnabled && control.Visibility == Visibility.Visible).ToArray();
    Assert(enabledStops.Length >= 8, "the main window exposes the primary controls as enabled tab stops");
    Assert(tabStops.Where(control => control.Visibility != Visibility.Visible).Any() == false, "tab stops are not hidden");
    Assert(enabledStops.OfType<Button>().All(button => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))), "enabled button tab stops have accessibility names");
}

static void AssertScaledLayout(MainWindow main)
{
    foreach (var scale in new[] { 1.25, 1.5 })
    {
        main.RootLayoutGrid.LayoutTransform = new ScaleTransform(scale, scale);
        main.UpdateLayout();
        Assert(main.RootLayoutGrid.ActualWidth > 0 && main.RootLayoutGrid.ActualHeight > 0, $"main layout remains measurable at {scale:0.##}x");
        foreach (var element in FindVisualElements(main.RootLayoutGrid))
        {
            if (element is FrameworkElement frameworkElement && frameworkElement.Visibility == Visibility.Visible)
                Assert(!double.IsNaN(frameworkElement.ActualWidth) && !double.IsNaN(frameworkElement.ActualHeight) && frameworkElement.ActualWidth >= 0 && frameworkElement.ActualHeight >= 0, $"visible control has valid dimensions at {scale:0.##}x");
        }
    }

    main.RootLayoutGrid.LayoutTransform = null;
    main.UpdateLayout();
}

static IEnumerable<DependencyObject> FindVisualElements(DependencyObject root)
{
    yield return root;
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        foreach (var child in FindVisualElements(VisualTreeHelper.GetChild(root, index)))
            yield return child;
    }
}

static IEnumerable<Control> FindTabStops(DependencyObject root)
{
    if (root is Control control && control.Focusable && control.IsTabStop)
        yield return control;

    foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
    {
        foreach (var tabStop in FindTabStops(child))
            yield return tabStop;
    }
}
