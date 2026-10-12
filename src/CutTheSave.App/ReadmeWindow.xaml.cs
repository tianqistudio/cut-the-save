using System.IO;
using System.Reflection;
using System.Windows;

namespace CutTheSave.App;

public partial class ReadmeWindow : Window
{
    public ReadmeWindow()
    {
        InitializeComponent();
        ReadmeTitle.Text = LocalizationService.Current == AppLanguage.EnUs ? "Cut the Save README" : LocalizationService.Current == AppLanguage.ZhTw ? "Cut the Save 使用說明" : "Cut the Save 使用说明";
        CloseButton.Content = LocalizationService.Current == AppLanguage.EnUs ? "Close" : LocalizationService.Current == AppLanguage.ZhTw ? "關閉" : "关闭";
        ReadmeText.Text = LoadGuide();
    }

    private static string LoadGuide()
    {
        var assembly = typeof(ReadmeWindow).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith("guide.md", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return "Cut the Save guide is unavailable in this build.";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream ?? throw new InvalidOperationException("Embedded guide resource could not be opened."));
        return reader.ReadToEnd();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
