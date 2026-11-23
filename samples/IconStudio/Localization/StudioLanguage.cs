using System.Globalization;
using TKCode.Arc.Localization;

namespace IconStudio.Localization;

internal static class StudioLanguage
{
    public static bool IsChinese => ArcLanguage.Current == LanguageTags.ZhCN;

    // Called by the desktop host only; embedded and headless hosts own their settings.
    public static void InitializeDesktop()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KubaiIcons.Avalonia");
        var path = Path.Combine(directory, "studio-language.txt");
        var initial = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? LanguageTags.ZhCN : LanguageTags.EnUS;
        try
        {
            if (File.Exists(path) && LanguageTag.TryParse(File.ReadAllText(path).Trim(), out var saved) &&
                (saved == LanguageTags.EnUS || saved == LanguageTags.ZhCN))
            {
                initial = saved;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not read Studio language: {0}", e.Message);
        }

        ArcLanguage.Set(initial);
        ArcLanguage.Changed += (_, e) =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, e.NewLanguage.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Could not save Studio language: {0}", ex.Message);
            }
        };
    }
}