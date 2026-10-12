using System.IO;
using CutTheSave.App;

internal static class IniLanguageChecks
{
    internal static void Select(AppLanguage language, string path)
    {
        // Use the real settings writer and startup reader, not OS input or a picker.
        LocalizationService.SetLanguage(language, path);
        var loaded = LocalizationService.LoadOrRecommend(path);
        if (loaded != language || !LocalizationService.HasSavedPreference)
            throw new InvalidOperationException("The isolated INI did not reload the selected language.");
        LocalizationService.UseLanguageForSession(loaded);
    }

    internal static void Run(string root)
    {
        var original = LocalizationService.Current;
        var path = Path.Combine(root, "language-only.ini");
        try
        {
            File.WriteAllText(path, "[General]\nLanguage=zh-CN\nTheme=Classic\nColorMode=Dark\nSavePath=C:\\Example\\save.bin\n[Future]\nKeep=1\n");
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                Select(language, path);
                if (AppSettingsFile.ReadGeneralValue("Theme", path) != "Classic"
                    || AppSettingsFile.ReadGeneralValue("ColorMode", path) != "Dark"
                    || AppSettingsFile.ReadGeneralValue("SavePath", path) != @"C:\Example\save.bin"
                    || !File.ReadAllText(path).Contains("Keep=1", StringComparison.Ordinal))
                    throw new InvalidOperationException("A language-only INI change altered unrelated settings.");
            }
            foreach (var pair in new[] { ("zhcn", AppLanguage.ZhCn), ("usen", AppLanguage.EnUs), ("zh_TW", AppLanguage.ZhTw), ("EN_us", AppLanguage.EnUs) })
            {
                AppSettingsFile.SetGeneralValue("Language", pair.Item1, path);
                if (LocalizationService.LoadOrRecommend(path) != pair.Item2)
                    throw new InvalidOperationException("The documented INI language alias was not recognized.");
            }
            Console.WriteLine("PASS INI language selection: three languages and aliases reload through startup settings, preserving theme/color/save/unknown keys without computer control");
        }
        finally { LocalizationService.UseLanguageForSession(original); LocalizationService.LoadOrRecommend(); }
    }
}
