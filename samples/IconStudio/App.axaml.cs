using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TKCode.Arc;
using TKCode.Arc.Fonts;
using TKCode.Arc.Localization;

namespace IconStudio;

public class App : Application
{
    /// <summary>
    /// Configures the shared desktop and headless hosts with the same Arc resources and fonts.
    /// </summary>
    public static AppBuilder CreateBuilder()
    {
        return AppBuilder.Configure<App>()
            .WithInterFont()
            .WithArcFonts()
            .UseArc(arc => arc.UseArcControls()
                .UseArcBlocks()
                .UseLanguages(LanguageTags.EnUS, [LanguageTags.EnUS, LanguageTags.ZhCN])
                .UsePackage("IconStudio", static p => TKCode.Arc.Generated.IconStudio.ArcGeneratedAssets.AddTo(p))
                .WithTheme(t => t.Appearance(ArcAppearance.Dark).Accent(ArcAccentPreset.Neutral)));
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Localization.StudioLanguage.InitializeDesktop();
            desktop.MainWindow = new StudioWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}