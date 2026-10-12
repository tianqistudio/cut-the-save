using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CutTheSave.App;

internal static class AppearancePreview
{
    // Render only our own unshown controls with synthetic entries. No input automation,
    // live save loading, Windows setting changes, or user preference writes occur here.
    internal static void Run(string outputDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var app = new App(suppressStartup: true) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var language in Enum.GetValues<AppLanguage>())
                {
                    IniLanguageChecks.Select(language, Path.Combine(outputDirectory, "CutTheSave.ini"));
                    foreach (var theme in Enum.GetValues<AppVisualTheme>())
                    foreach (var mode in new[] { AppColorMode.Light, AppColorMode.Dark })
                    {
                        AppearanceService.ApplyForSession(theme, mode);
                        var name = $"{theme}-{mode}-{LocalizationService.ToCode(language)}";
                        var main = new MainWindow(suppressStartupDetection: true);
                        main.EntriesGrid.ItemsSource = new[]
                        {
                            new { Kind = "int", Key = "STARS_0_0", Value = "3" },
                            new { Kind = "int", Key = "SCORE_0_0", Value = "1000" },
                            new { Kind = "int", Key = "UNLOCKED_0_1", Value = "1" }
                        };
                        // Do not expose previously observed local paths or runtime details in previews.
                        main.RtssProfileStatusText.Text = LocalizationService.Get("RtssProfileNotRead");
                        Render(main, 1320, 810, Path.Combine(outputDirectory, name + ".png"));
                        Render(main, 720, 420, Path.Combine(outputDirectory, name + "-minimum.png"));
                        AssertHeaderFits(main);
                        main.Close();
                        var picker = new AppearanceWindow();
                        Render(picker, 544, 531, Path.Combine(outputDirectory, name + "-picker.png"));
                        AssertInside(picker.ApplyButton, (FrameworkElement)picker.Content, "appearance apply button");
                        picker.Close();
                    }
                }

                // Exercise the same dispatcher-side update used by Windows notifications
                // while the window contains an unsaved synthetic edit.
                var draftWindow = new MainWindow(suppressStartupDetection: true);
                var draftRows = new[] { new { Kind = "int", Key = "STARS_0_0", Value = "2" } };
                draftWindow.EntriesGrid.ItemsSource = draftRows;
                draftWindow.RtssProfileStatusText.Text = LocalizationService.Get("RtssProfileNotRead");
                AppearanceService.ApplyForSession(AppVisualTheme.Windows11, AppColorMode.System);
                AppearanceService.RefreshSystemColorMode(false);
                var light = ResourceColor("PageBackgroundBrush");
                AppearanceService.RefreshSystemColorMode(true);
                var dark = ResourceColor("PageBackgroundBrush");
                if (light == dark || AppearanceService.EffectiveColorMode != AppColorMode.Dark)
                    throw new InvalidOperationException("Automatic appearance did not react to system color changes.");
                if (!ReferenceEquals(draftWindow.EntriesGrid.ItemsSource, draftRows) || draftRows[0].Value != "2")
                    throw new InvalidOperationException("Automatic appearance replaced the in-memory draft.");
                AppearanceService.ApplyForSession(AppVisualTheme.Windows11, AppColorMode.Light);
                AppearanceService.RefreshSystemColorMode(true);
                if (AppearanceService.EffectiveColorMode != AppColorMode.Light)
                    throw new InvalidOperationException("A late Windows event overrode the manual color selection.");
                draftWindow.Close();
                app.Shutdown();
                Console.WriteLine("PASS 36 offscreen theme/language/minimum-layout previews; automatic switching and manual override checks");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private static Color ResourceColor(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    private static void AssertHeaderFits(MainWindow window)
    {
        var root = window.RootLayoutGrid;
        foreach (var button in new[] { window.ReadmeButton, window.LanguageButton, window.AppearanceButton, window.SupportButton, window.UpdateButton })
            AssertInside(button, root, button.Name);
        if (window.SubtitleText.Visibility == Visibility.Visible)
        {
            var subtitle = new Rect(window.SubtitleText.TranslatePoint(new Point(), root), window.SubtitleText.RenderSize);
            subtitle.Inflate(-1, -1);
            foreach (var button in new[] { window.ReadmeButton, window.LanguageButton, window.AppearanceButton, window.SupportButton, window.UpdateButton })
                if (subtitle.IntersectsWith(new Rect(button.TranslatePoint(new Point(), root), button.RenderSize)))
                    throw new InvalidOperationException("Header subtitle overlaps an action button.");
        }
    }

    private static void AssertInside(FrameworkElement element, FrameworkElement root, string name)
    {
        var position = element.TranslatePoint(new Point(), root);
        if (position.X < -1 || position.Y < -1 || position.X + element.ActualWidth > root.ActualWidth + 1 || position.Y + element.ActualHeight > root.ActualHeight + 1)
            throw new InvalidOperationException($"{name} is clipped in the rendered layout.");
    }

    internal static void Render(Window window, double width, double height, string path)
    {
        if (window is MainWindow compact) compact.UpdateCompactLayout(width, height);
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content };
        host.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
        host.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, window.FontFamily);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        foreach (var button in Descendants(host).OfType<Button>())
        foreach (var label in Descendants(button).OfType<TextBlock>())
        {
            if (label.Text != button.Content?.ToString())
                continue;
            if (((SolidColorBrush)label.Foreground).Color != ((SolidColorBrush)button.Foreground).Color)
                throw new InvalidOperationException("Button label lost its accent or disabled foreground color.");
        }
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        if (window is MainWindow main)
        {
            AssertHeaderFits(main);
            if (((SolidColorBrush)main.TitleText.Foreground).Color != ResourceColor("TextBrush"))
                throw new InvalidOperationException("Main title does not use the theme text color.");
        }
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        host.Child = null;
        window.Content = content;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        foreach (var descendant in Descendants(VisualTreeHelper.GetChild(root, index)))
            yield return descendant;
    }
}
