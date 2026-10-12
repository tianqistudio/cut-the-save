using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using CutTheSave.App;
using CutTheSave.Core;

internal static class UiOptimizationChecks
{
    public static void Run(MainWindow window)
    {
        var originalWidth = window.Width;
        var originalHeight = window.Height;
        var language = LocalizationService.Current;
        foreach (var size in new[] { new Size(720,420), new Size(960,540), new Size(1092,614), new Size(1320,840) })
        foreach (var lang in Enum.GetValues<AppLanguage>())
        {
            LocalizationService.UseLanguageForSession(lang);
            window.ApplyLanguage();
            window.Width=size.Width; window.Height=size.Height;
            window.UpdateCompactLayout(size.Width,size.Height);
            window.UpdateLayout();
            foreach (var button in new[]{window.ReadmeButton,window.LanguageButton,window.AppearanceButton,window.SupportButton,window.UpdateButton})
            {
                var bounds=button.TransformToAncestor(window.RootLayoutGrid).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Left>=-1 && bounds.Right<=window.RootLayoutGrid.ActualWidth+1 && bounds.Bottom<=window.RootLayoutGrid.ActualHeight+1,"toolbar is within the viewport");
            }
            Check(window.EntriesGrid.ActualHeight>30 && window.EntriesGrid.ActualWidth>160,$"editor remains usable in compact view: {size} {lang}, actual={window.EntriesGrid.ActualWidth}x{window.EntriesGrid.ActualHeight}");
            window.BackupsButton.BringIntoView(); window.UpdateLayout();
            Check(window.BackupsButton.IsVisible,"sidebar action remains scroll-accessible");
        }
        window.Width=originalWidth; window.Height=originalHeight;
        window.UpdateCompactLayout(originalWidth,originalHeight);
        LocalizationService.UseLanguageForSession(AppLanguage.EnUs);
        var save=new CutTheRopeSave();save.SetInt("UNLOCKED_0_0",1);save.SetInt("STARS_0_0",1);save.SetInt("SCORE_0_0",100);
        window.AcceptLoadedSave(save,"C:\\synthetic-only\\ctr_save.bin");
        var row=window.LevelsGrid.ItemsSource.Cast<MainWindow.LevelRow>().First();
        Check(window.TryApplyLevelEdit(row,3,"2"),"edit accepted");
        Check(window.DirtyText.Visibility==Visibility.Visible,"dirty state persists independently of status text");
        var notified=false;row.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(row.Changed)) notified=true; };
        LocalizationService.UseLanguageForSession(AppLanguage.ZhCn);window.ApplyLanguage();
        Check(notified && row.Changed=="是" && ReferenceEquals(row,window.LevelsGrid.ItemsSource.Cast<MainWindow.LevelRow>().First()),"language refresh preserves draft and row identity");
        window.AcceptSavedDraft(window.BuildEditedSave(out _),"C:\\synthetic-only\\ctr_save.bin");
        Check(window.DirtyText.Visibility==Visibility.Collapsed,"saving clears persistent dirty state");
        using var release = new ManualResetEventSlim();
        var uiThread=Environment.CurrentManagedThreadId;
        var operation=window.RunOperationAsync(()=> { release.Wait();return Environment.CurrentManagedThreadId; });
        Check(window.IsOperationBusy && !window.RootLayoutGrid.IsEnabled,"operation disables duplicate actions");
        release.Set();
        PumpUntil(operation,window.Dispatcher);
        Check(operation.GetAwaiter().GetResult()!=uiThread && !window.IsOperationBusy && window.RootLayoutGrid.IsEnabled,"transaction executes off UI thread and re-enables controls");
        var failed=window.RunOperationAsync<int>(()=>throw new IOException("synthetic failure"));
        PumpUntil(failed,window.Dispatcher);
        try { failed.GetAwaiter().GetResult(); } catch(IOException) { }
        Check(!window.IsOperationBusy && window.RootLayoutGrid.IsEnabled,"failure releases busy state");
        LocalizationService.UseLanguageForSession(language);window.ApplyLanguage();
        Console.WriteLine("PASS compact viewport matrix, language/dirty state and asynchronous operation lifetime");
    }
    private static void PumpUntil(Task task,Dispatcher dispatcher)
    {
        var frame=new DispatcherFrame();
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
    }
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
}
