using System.IO;
using System.Windows;
using CutTheSave.Core;

namespace CutTheSave.App;

public partial class MainWindow
{
    internal bool IsOperationBusy { get; private set; }

    internal async Task<T> RunOperationAsync<T>(Func<T> work)
    {
        if (IsOperationBusy) throw new InvalidOperationException(LocalizationService.Get("OperationBusy"));
        IsOperationBusy = true;
        RootLayoutGrid.IsEnabled = false;
        var previousStatus = _statusTextFactory;
        SetStatus("OperationBusy");
        var busyStatus = _statusTextFactory;
        try { return await Task.Run(work); }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                IsOperationBusy = false;
                RootLayoutGrid.IsEnabled = true;
                if (_statusTextFactory == busyStatus) SetStatus(previousStatus);
            });
        }
    }

    private void RefreshDirtyIndicator()
    {
        DirtyText.Text = LocalizationService.Get("UnsavedEditsTitle");
        DirtyText.Visibility = HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void UpdateCompactLayout(double width, double height)
    {
        RootLayoutGrid.Margin = new Thickness(width < 1100 ? 14 : 30);
        SidebarColumn.Width = new GridLength(width < 1100 ? 230 : 300);
        SidebarGap.Width = new GridLength(width < 1100 ? 12 : 20);
        SubtitleText.Visibility = height < 540 ? Visibility.Collapsed : Visibility.Visible;
        SummaryCountsGrid.Visibility = height < 640 ? Visibility.Collapsed : Visibility.Visible;
        EntriesTitleText.Visibility = height < 500 ? Visibility.Collapsed : Visibility.Visible;
        GuideBorder.Visibility = height < 680 ? Visibility.Collapsed : Visibility.Visible;
    }

}
