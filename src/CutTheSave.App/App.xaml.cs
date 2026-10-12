namespace CutTheSave.App;

public partial class App : System.Windows.Application
{
    private readonly bool _suppressStartup;

    public App() : this(suppressStartup: false) { }

    internal App(bool suppressStartup)
    {
        _suppressStartup = suppressStartup;
        InitializeComponent();
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_suppressStartup)
            return;
        var helperInvocation = RtssHelperProtocol.Parse(e.Args);
        if (helperInvocation.IsHelperMode)
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var exitCode = helperInvocation.IsValid
                ? RtssElevatedCommandRunner.Run(helperInvocation)
                : RtssHelperExitCode.InvalidArguments;
            Shutdown((int)exitCode);
            return;
        }

        AppearanceService.Initialize();
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        var language = LocalizationService.LoadOrRecommend();
        if (!LocalizationService.HasSavedPreference)
        {
            var picker = new LanguageSelectionWindow(
                language,
                LocalizationService.RecommendForCurrentSystem(),
                System.Globalization.CultureInfo.CurrentUICulture.Name);
            if (picker.ShowDialog() == true)
                language = picker.SelectedLanguage;
        }

        LocalizationService.SetLanguage(language);
        MainWindow = new MainWindow();
        MainWindow.Show();
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppearanceService.Shutdown();
        base.OnExit(e);
    }
}
