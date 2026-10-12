using System.Globalization;
using System.Windows;

namespace CutTheSave.App;

public partial class LanguageSelectionWindow : Window
{
    private readonly AppLanguage _recommended;
    private readonly AppLanguage _displayLanguage;
    private readonly string _detectedCultureName;
    public AppLanguage SelectedLanguage { get; private set; }

    public LanguageSelectionWindow(AppLanguage selectedLanguage)
        : this(selectedLanguage, LocalizationService.RecommendForCurrentSystem(), CultureInfo.CurrentUICulture.Name)
    {
    }

    internal LanguageSelectionWindow(AppLanguage selectedLanguage, AppLanguage recommended, string detectedCultureName)
    {
        InitializeComponent();
        _recommended = recommended;
        _displayLanguage = selectedLanguage;
        _detectedCultureName = detectedCultureName;
        SelectedLanguage = selectedLanguage;
        LanguageComboBox.ItemsSource = new[] { "简体中文", "繁體中文", "English" };
        LanguageComboBox.SelectedIndex = (int)selectedLanguage;
        ApplyText();
    }

    private void ApplyText()
    {
        var code = LocalizationService.ToCode(_recommended);
        Title = LocalizationService.Get("LanguageDialogTitle", _displayLanguage);
        TitleText.Text = Title;
        LanguageLabel.Text = LocalizationService.Get("LanguageDialogLabel", _displayLanguage);
        RecommendationText.Text = LocalizationService.Format(
            "LanguageRecommendation",
            _displayLanguage,
            _detectedCultureName,
            LocalizationService.GetLanguageName(_recommended, _displayLanguage),
            code);
        UseRecommendedButton.Content = LocalizationService.Get("UseRecommendedLanguage", _displayLanguage);
        ConfirmButton.Content = LocalizationService.Get("ConfirmLanguage", _displayLanguage);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedLanguage = (AppLanguage)LanguageComboBox.SelectedIndex;
        DialogResult = true;
    }

    private void UseRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedLanguage = _recommended;
        DialogResult = true;
    }
}
