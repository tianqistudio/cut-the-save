using System.Windows;
using System.Windows.Controls;

namespace CutTheSave.App;

public partial class AppearanceWindow : Window
{
    public AppearanceWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        ThemeComboBox.SelectedIndex = AppearanceService.CurrentTheme == AppVisualTheme.Classic ? 1 : 0;
        ColorModeComboBox.SelectedIndex = AppearanceService.SelectedColorMode switch
        {
            AppColorMode.Light => 1,
            AppColorMode.Dark => 2,
            _ => 0
        };
        RefreshPreview();
    }

    private void ApplyLanguage()
    {
        Title = LocalizationService.Get("AppearanceDialogTitle");
        TitleText.Text = Title;
        HelpText.Text = LocalizationService.Get("AppearanceHelp");
        ThemeLabel.Text = LocalizationService.Get("ThemeStyleLabel");
        ColorModeLabel.Text = LocalizationService.Get("ColorModeLabel");
        System.Windows.Automation.AutomationProperties.SetName(ThemeComboBox, ThemeLabel.Text);
        System.Windows.Automation.AutomationProperties.SetName(ColorModeComboBox, ColorModeLabel.Text);
        ThemeComboBox.ItemsSource = new[]
        {
            LocalizationService.Get("ThemeWindows11"),
            LocalizationService.Get("ThemeClassic")
        };
        ColorModeComboBox.ItemsSource = new[]
        {
            LocalizationService.Get("ColorSystem"),
            LocalizationService.Get("ColorLight"),
            LocalizationService.Get("ColorDark")
        };
        CancelButton.Content = LocalizationService.Get("Cancel");
        ApplyButton.Content = LocalizationService.Get("ApplyAppearance");
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (ThemeComboBox.SelectedIndex < 0 || ColorModeComboBox.SelectedIndex < 0)
            return;

        var theme = SelectedTheme();
        var mode = SelectedColorMode();
        var effective = AppearanceService.ResolveEffectiveColorMode(mode, AppearanceService.SystemUsesDarkMode());
        PreviewText.Text = LocalizationService.Format(
            "AppearancePreview",
            ThemeName(theme),
            ColorModeName(mode),
            ColorModeName(effective));
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppearanceService.SetAppearance(SelectedTheme(), SelectedColorMode());
            DialogResult = true;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            PreviewText.Text = LocalizationService.Get("AppearanceSaveFailed");
            PreviewText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DangerBrush");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private AppVisualTheme SelectedTheme() => ThemeComboBox.SelectedIndex == 1
        ? AppVisualTheme.Classic
        : AppVisualTheme.Windows11;

    private AppColorMode SelectedColorMode() => ColorModeComboBox.SelectedIndex switch
    {
        1 => AppColorMode.Light,
        2 => AppColorMode.Dark,
        _ => AppColorMode.System
    };

    private static string ThemeName(AppVisualTheme theme) => LocalizationService.Get(
        theme == AppVisualTheme.Classic ? "ThemeClassic" : "ThemeWindows11");

    private static string ColorModeName(AppColorMode mode) => LocalizationService.Get(mode switch
    {
        AppColorMode.Light => "ColorLight",
        AppColorMode.Dark => "ColorDark",
        _ => "ColorSystem"
    });
}
