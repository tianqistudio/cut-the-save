using System.Windows;
using CutTheSave.Infrastructure.Windows;

namespace CutTheSave.App;

public partial class GameInstallationPickerWindow : Window
{
    public GameInstallation? SelectedInstallation { get; private set; }

    public GameInstallationPickerWindow(IReadOnlyList<GameInstallation> installations)
    {
        InitializeComponent();
        InstallationsList.ItemsSource = installations.Select(x => new InstallationItem(x)).ToArray();
        InstallationsList.SelectedIndex = installations.Count > 0 ? 0 : -1;
        if (LocalizationService.Current == AppLanguage.EnUs)
        {
            TitleText.Text = "Choose game location";
            HelpText.Text = "Multiple Steam libraries were found. Choose the Cut the Rope installation to use.";
            CancelButton.Content = "Cancel";
            ConfirmButton.Content = "Use this location";
        }
        else if (LocalizationService.Current == AppLanguage.ZhTw)
        {
            TitleText.Text = "選擇遊戲位置";
            HelpText.Text = "偵測到多個 Steam 程式庫，請選擇要使用的 Cut the Rope。";
            CancelButton.Content = "取消";
            ConfirmButton.Content = "使用此位置";
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedInstallation = (InstallationsList.SelectedItem as InstallationItem)?.Installation;
        if (SelectedInstallation is not null)
            DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record InstallationItem(GameInstallation Installation)
    {
        public string DisplayText => $"{Installation.ExecutablePath}\nSteam library: {Installation.LibraryPath}";
    }
}
