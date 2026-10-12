using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace CutTheSave.App;

public enum AppVisualTheme
{
    Windows11,
    Classic
}

public enum AppColorMode
{
    System,
    Light,
    Dark
}

public readonly record struct AppearancePreference(AppVisualTheme Theme, AppColorMode ColorMode);

public static class AppearanceService
{
    private const string ThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private static bool _initialized;
    private static bool _windowHandlerRegistered;
    private static bool? _systemDarkOverride;

    public static AppVisualTheme CurrentTheme { get; private set; } = AppVisualTheme.Windows11;
    public static AppColorMode SelectedColorMode { get; private set; } = AppColorMode.System;
    public static AppColorMode EffectiveColorMode { get; private set; } = AppColorMode.Light;
    public static event EventHandler? AppearanceChanged;

    public static AppearancePreference LoadOrDefault()
    {
        var theme = ParseTheme(AppSettingsFile.ReadGeneralValue("Theme"));
        var colorMode = ParseColorMode(AppSettingsFile.ReadGeneralValue("ColorMode"));
        return new AppearancePreference(theme, colorMode);
    }

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        if (!_windowHandlerRegistered)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(Window_Loaded));
            _windowHandlerRegistered = true;
        }
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        var preference = LoadOrDefault();
        ApplyForSession(preference.Theme, preference.ColorMode);
    }

    public static void Shutdown()
    {
        if (!_initialized)
            return;

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _initialized = false;
    }

    public static void SetAppearance(AppVisualTheme theme, AppColorMode colorMode)
    {
        AppSettingsFile.SetGeneralValues(new Dictionary<string, string?>
        {
            ["Theme"] = ToCode(theme),
            ["ColorMode"] = ToCode(colorMode)
        });
        ApplyForSession(theme, colorMode);
    }

    internal static void ApplyForSession(AppVisualTheme theme, AppColorMode colorMode, bool? systemDarkOverride = null)
    {
        CurrentTheme = theme;
        SelectedColorMode = colorMode;
        _systemDarkOverride = systemDarkOverride;
        EffectiveColorMode = ResolveEffectiveColorMode(colorMode, systemDarkOverride ?? SystemUsesDarkMode());
        ApplyResources();
        AppearanceChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static AppVisualTheme ParseTheme(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "classic" => AppVisualTheme.Classic,
        _ => AppVisualTheme.Windows11
    };

    internal static AppColorMode ParseColorMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => AppColorMode.Light,
        "dark" => AppColorMode.Dark,
        _ => AppColorMode.System
    };

    internal static AppColorMode ResolveEffectiveColorMode(AppColorMode selected, bool systemUsesDarkMode) => selected switch
    {
        AppColorMode.Light => AppColorMode.Light,
        AppColorMode.Dark => AppColorMode.Dark,
        _ => systemUsesDarkMode ? AppColorMode.Dark : AppColorMode.Light
    };

    internal static string ToCode(AppVisualTheme theme) => theme == AppVisualTheme.Classic ? "Classic" : "Windows11";
    internal static string ToCode(AppColorMode mode) => mode switch
    {
        AppColorMode.Light => "Light",
        AppColorMode.Dark => "Dark",
        _ => "System"
    };

    internal static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ThemeRegistryPath, writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted)
            return;

        _ = app.Dispatcher.BeginInvoke(new Action(() => RefreshSystemColorMode(SystemUsesDarkMode())));
    }

    internal static void RefreshSystemColorMode(bool systemUsesDarkMode)
    {
        // Recheck on the UI thread: a queued Windows event must not undo a later manual selection.
        if (SelectedColorMode != AppColorMode.System || _systemDarkOverride.HasValue)
            return;
        var effective = ResolveEffectiveColorMode(AppColorMode.System, systemUsesDarkMode);
        if (effective == EffectiveColorMode)
            return;
        EffectiveColorMode = effective;
        ApplyResources();
        AppearanceChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyResources()
    {
        var app = Application.Current;
        if (app is null)
            return;

        // Use WPF's built-in Fluent base for system controls (including scroll bars).
        // The API is still annotated experimental in .NET 10; keep that opt-in local.
#pragma warning disable WPF0001
        app.ThemeMode = EffectiveColorMode == AppColorMode.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        var palette = GetPalette(CurrentTheme, EffectiveColorMode);
        SetBrush(app.Resources, "PageBackgroundBrush", palette.Page);
        SetBrush(app.Resources, "CardBackgroundBrush", palette.Card);
        SetBrush(app.Resources, "ControlBackgroundBrush", palette.Control);
        SetBrush(app.Resources, "ControlHoverBrush", palette.ControlHover);
        SetBrush(app.Resources, "ControlPressedBrush", palette.ControlPressed);
        SetBrush(app.Resources, "ControlDisabledBrush", palette.ControlDisabled);
        SetBrush(app.Resources, "PrimaryBrush", palette.Primary);
        SetBrush(app.Resources, "PrimaryHoverBrush", palette.PrimaryHover);
        SetBrush(app.Resources, "PrimaryPressedBrush", palette.PrimaryPressed);
        SetBrush(app.Resources, "PrimaryForegroundBrush", palette.PrimaryForeground);
        SetBrush(app.Resources, "TextBrush", palette.Text);
        SetBrush(app.Resources, "SecondaryTextBrush", palette.SecondaryText);
        SetBrush(app.Resources, "DisabledTextBrush", palette.DisabledText);
        SetBrush(app.Resources, "BorderBrush", palette.Border);
        SetBrush(app.Resources, "StrongBorderBrush", palette.StrongBorder);
        SetBrush(app.Resources, "AccentSubtleBrush", palette.AccentSubtle);
        SetBrush(app.Resources, "AccentSubtleBorderBrush", palette.AccentSubtleBorder);
        SetBrush(app.Resources, "AccentSubtleTextBrush", palette.AccentSubtleText);
        SetBrush(app.Resources, "SelectionBrush", palette.Selection);
        SetBrush(app.Resources, "SuccessBrush", palette.Success);
        SetBrush(app.Resources, "DangerBrush", palette.Danger);
        SetBrush(app.Resources, "DataGridAlternateBrush", palette.DataGridAlternate);

        ReplaceThemeDictionary(app.Resources, CurrentTheme);
        foreach (Window window in app.Windows)
            ApplyWindowChrome(window);
    }

    private static void ReplaceThemeDictionary(ResourceDictionary resources, AppVisualTheme theme)
    {
        var fileName = theme == AppVisualTheme.Classic ? "Classic.xaml" : "Win11.xaml";
        var existing = resources.MergedDictionaries
            .Where(dictionary => dictionary.Source?.OriginalString.EndsWith("Themes/Win11.xaml", StringComparison.OrdinalIgnoreCase) == true ||
                                 dictionary.Source?.OriginalString.EndsWith("Themes/Classic.xaml", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (existing.Length == 1 && existing[0].Source.OriginalString.EndsWith($"Themes/{fileName}", StringComparison.OrdinalIgnoreCase))
            return;
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/CutTheSave;component/Themes/{fileName}", UriKind.Absolute)
        };
        foreach (var dictionary in existing)
            resources.MergedDictionaries.Remove(dictionary);
        resources.MergedDictionaries.Add(replacement);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string value)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }

    private static Palette GetPalette(AppVisualTheme theme, AppColorMode mode)
    {
        if (theme == AppVisualTheme.Classic && mode == AppColorMode.Light)
        {
            return new Palette(
                "#F6F7F9", "#FFFFFF", "#F4F5F7", "#E9EDF2", "#DDE3EA", "#E4E7EB",
                "#2457A6", "#1E4B91", "#173D78", "#FFFFFF", "#1B1F24", "#59636F", "#8B949E",
                "#D8DEE8", "#AEB8C5", "#EAF2FF", "#C8D9F5", "#274F85", "#B8D6FF", "#207544", "#B42318", "#FAFBFC");
        }

        if (theme == AppVisualTheme.Classic)
        {
            return new Palette(
                "#1E2228", "#292E35", "#343A42", "#3D454F", "#24292F", "#30363D",
                "#79A8F7", "#91B8FA", "#5F91E3", "#101317", "#F2F4F7", "#BBC4CF", "#7E8996",
                "#46505C", "#667383", "#263B59", "#3E5D88", "#D7E6FC", "#315B8F", "#6FD39A", "#FF8E84", "#252A31");
        }

        if (mode == AppColorMode.Dark)
        {
            return new Palette(
                "#202020", "#2B2B2B", "#323232", "#3B3B3B", "#272727", "#2A2A2A",
                "#60CDFF", "#7AD7FF", "#4BBCEA", "#00364D", "#FFFFFF", "#C9C9C9", "#777777",
                "#454545", "#666666", "#24353D", "#3E5966", "#D6F2FF", "#315D73", "#6CCB8E", "#FF99A4", "#272727");
        }

        return new Palette(
            "#F3F3F3", "#FBFBFB", "#FFFFFF", "#F6F6F6", "#EDEDED", "#F0F0F0",
            "#0067C0", "#1975C5", "#00589F", "#FFFFFF", "#1A1A1A", "#5D5D5D", "#9A9A9A",
            "#D1D1D1", "#8A8A8A", "#E7F2FA", "#B8D8EE", "#17496A", "#B7DDF5", "#0F7B42", "#C42B1C", "#F7F7F7");
    }

    private static void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            ApplyWindowChrome(window);
    }

    private static void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var dark = EffectiveColorMode == AppColorMode.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref dark, sizeof(int));

        var cornerPreference = CurrentTheme == AppVisualTheme.Windows11 ? 2 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);

    private sealed record Palette(
        string Page,
        string Card,
        string Control,
        string ControlHover,
        string ControlPressed,
        string ControlDisabled,
        string Primary,
        string PrimaryHover,
        string PrimaryPressed,
        string PrimaryForeground,
        string Text,
        string SecondaryText,
        string DisabledText,
        string Border,
        string StrongBorder,
        string AccentSubtle,
        string AccentSubtleBorder,
        string AccentSubtleText,
        string Selection,
        string Success,
        string Danger,
        string DataGridAlternate);
}
