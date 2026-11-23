using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using IconStudio;
using KubaiIcons.Avalonia;
using NUnit.Framework;
using TKCode.Arc.Motion;
using Arc = TKCode.Arc;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test, Order(7)]
    public async Task Studio_WhenUsingChromeAndSizeSlider_KeepsWindowActionsAndLivePreviewUsable()
    {
        await _session.Dispatch(
            () =>
            {
                var window = new StudioWindow
                {
                    Width = 1100,
                    Height = 740
                };
                ArcMotion.SetLevel(window, ArcMotionLevel.Off);
                T Find<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new AssertionException(name);

                Point Center(Control control)
                {
                    return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                    ?? throw new AssertionException("Detached control.");
                }

                void Click(Control control)
                {
                    var point = Center(control);
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Pump();
                }

                void Press(Key key)
                {
                    window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                    Pump();
                }

                try
                {
                    window.Show();
                    Pump();
                    var repository = Find<HyperlinkButton>("RepositoryLink");
                    Assert.That(window.Title, Is.EqualTo("Kubai Icons Avalonia"));
                    Assert.That(window.Icon, Is.Not.Null);
                    Assert.That(repository.NavigateUri?.AbsoluteUri, Is.EqualTo("https://github.com/Sylinko/KubaiIcons.Avalonia"));
                    Assert.That(Find<PathIcon>("RepositoryMark").Data, Is.Not.Null);
                    Assert.That(Center(repository).X, Is.LessThan(Center(Find<Arc.ThemeSwitcher>("ThemeToggle")).X));
                    var themeButton = Find<Arc.ThemeSwitcher>("ThemeToggle").GetVisualDescendants().OfType<Arc.Button>().Single();
                    var languageButton = Find<Arc.Button>("LanguageButton");
                    var bar = window.TitleBar ?? throw new AssertionException("Missing Arc title bar.");
                    var brand = Find<StackPanel>("Brand");
                    var sidebar = Find<Arc.Sidebar>("Categories");
                    Assert.That(window.ExtendClientAreaToDecorationsHint && window.IsTitleBarVisible, Is.True);
                    Assert.That(bar.Title, Is.Empty);
                    Assert.That(bar.BorderThickness, Is.EqualTo(default(Thickness)));
                    Assert.That((bar.Background as ISolidColorBrush)?.Color, Is.EqualTo(((ISolidColorBrush)sidebar.Background!).Color));
                    Assert.That(Center(brand).Y, Is.EqualTo(Center(bar).Y).Within(1));
                    Assert.That(sidebar.TranslatePoint(default, window)?.Y, Is.EqualTo(bar.Bounds.Bottom).Within(1));
                    Assert.That(bar.CaptionButtons, Is.EqualTo(Arc.TitleBarCaptionButtons.Auto));

                    // Exercise Arc's native traffic-light layout even on a Windows or Linux test host.
                    bar.CaptionButtons = Arc.TitleBarCaptionButtons.System;
                    Pump();
                    var inset = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SystemInset");
                    Assert.That(inset.IsEffectivelyVisible, Is.True);
                    Assert.That(brand.TranslatePoint(default, window)!.Value.X,
                        Is.GreaterThan(inset.TranslatePoint(new Point(inset.Bounds.Width, 0), window)!.Value.X));
                    window.WindowState = WindowState.FullScreen;
                    Pump();
                    Assert.That(inset.IsEffectivelyVisible, Is.False);
                    window.WindowState = WindowState.Normal;
                    bar.CaptionButtons = Arc.TitleBarCaptionButtons.Drawn;
                    Pump();
                    var captions = bar.GetVisualDescendants()
                        .OfType<Button>()
                        .Where(b => b.Name is
                            Arc.WindowCaptionButtons.MinimizeButtonPart or Arc.WindowCaptionButtons.MaximizeButtonPart or
                            Arc.WindowCaptionButtons.CloseButtonPart)
                        .ToArray();
                    Assert.That(captions.Length, Is.EqualTo(3));
                    foreach (var action in new Button[] { repository, languageButton, themeButton })
                    {
                        Assert.That(action.Bounds.Size, Is.EqualTo(captions[0].Bounds.Size));
                        Assert.That(action.CornerRadius.TopLeft, Is.GreaterThanOrEqualTo(action.Bounds.Height / 2));
                        Assert.That(Center(action).Y, Is.EqualTo(Center(captions[0]).Y).Within(1));
                        Assert.That((action.Background as ISolidColorBrush)?.Color.A, Is.EqualTo(0));
                        window.MouseMove(Center(action));
                        Pump();
                        Assert.That((action.Background as ISolidColorBrush)?.Color.A, Is.GreaterThan(0));
                        window.MouseMove(new Point(2, 2));
                        Pump();
                    }
                    Assert.That(Center(languageButton).X - Center(repository).X,
                        Is.EqualTo(Center(themeButton).X - Center(languageButton).X).Within(1));
                    Assert.That(captions.All(b => b.IsEffectivelyVisible && b.IsEnabled), Is.True);
                    Click(captions.Single(b => b.Name == Arc.WindowCaptionButtons.MaximizeButtonPart));
                    Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
                    Click(captions.Single(b => b.Name == Arc.WindowCaptionButtons.MaximizeButtonPart));
                    Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));

                    Click(Find<Button>("AboutTrigger"));
                    var about = Find<Arc.Popover>("About");
                    Assert.That(about.IsOpen, Is.True);
                    var links = window.GetVisualDescendants().OfType<HyperlinkButton>().Where(b => b.Classes.Contains("credit")).ToArray();
                    Assert.That(
                        links.Select(b => b.NavigateUri?.AbsoluteUri),
                        Is.EquivalentTo(
                            new[]
                            {
                                "https://github.com/kubai087", "https://github.com/Sylinko",
                                "https://github.com/Sylinko/KubaiIcons.Avalonia/blob/main/LICENSE"
                            }));
                    Capture(window, "studio-about");
                    Press(Key.Escape);
                    Assert.That(about.IsOpen, Is.False);
                    Assert.That(window.FocusManager.GetFocusedElement(), Is.SameAs(Find<Button>("AboutTrigger")));

                    Find<TextBox>("Search").Text = "award";
                    Pump();
                    var library = Find<ListBox>("Library");
                    var tile = library.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is StudioEntry { Name: "award" });
                    var entries = library.Items.Cast<StudioRow>().SelectMany(r => r.Icons).ToArray();
                    var galleryIcons = library.GetVisualDescendants().OfType<KubaiIcon>().ToArray();
                    Assert.That(galleryIcons, Is.Not.Empty);
                    Click(tile);
                    var inspector = Find<ContentControl>("DetailHost").Content as IconInspector ?? throw new AssertionException("No inspector.");
                    var icon = inspector.FindControl<KubaiIcon>("Specimen") ?? throw new AssertionException("No specimen.");
                    var code = inspector.FindControl<Arc.CodeBlock>("Snippet") ?? throw new AssertionException("No source.");
                    var size = Find<Arc.Slider>("IconSize");
                    Assert.That(size.Value, Is.EqualTo(32));
                    Assert.That(Center(Find<TextBlock>("SizeLabel")).Y, Is.EqualTo(Center(Find<TextBlock>("SizeValue")).Y).Within(1));
                    Assert.That(Find<TextBlock>("SizeValue").Text, Is.EqualTo("32 DIP"));
                    Assert.That(size.Bounds.Height, Is.LessThanOrEqualTo(44), "Size stays on a single toolbar row.");
                    var thumb = size.GetVisualDescendants()
                        .OfType<Control>()
                        .Single(c => c.Focusable && c.IsEffectivelyVisible && c.GetType().Name == "SliderThumb");
                    ArcMotion.SetLevel(window, ArcMotionLevel.Full);
                    var previewSize = icon.Size;
                    var previewRect = PixelRect.FromRect(
                        new Rect(
                            icon.TranslatePoint(default, window)
                            ?? throw new AssertionException("Detached preview."),
                            icon.Bounds.Size),
                        window.RenderScaling);
                    var sourceText = code.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Text");
                    var start = Center(thumb);
                    var end = start + new Vector(32, 0);
                    window.MouseMove(start);
                    window.MouseDown(start, MouseButton.Left);
                    Pump();
                    var preview = ReadRegion(window, previewRect);

                    void AssertStablePreview()
                    {
                        var origin = icon.TranslatePoint(default, window) ?? throw new AssertionException("Detached preview.");
                        Assert.That(PixelRect.FromRect(new Rect(origin, icon.Bounds.Size), window.RenderScaling), Is.EqualTo(previewRect));
                        // Skia's soft shadow can round a color channel differently when redrawing a dirty region.
                        var pixels = ReadRegion(window, previewRect);
                        Assert.That(pixels.Zip(preview, (a, b) => Math.Abs(a - b)).Max(), Is.LessThanOrEqualTo(3));
                    }

                    window.MouseMove(end);
                    Pump();
                    Assert.That(size.Value, Is.GreaterThan(32), "Dragging updates the value before pointer release.");
                    Assert.That(icon.Size, Is.EqualTo(previewSize));
                    AssertStablePreview();
                    Assert.That(sourceText.Opacity, Is.EqualTo(1));
                    Assert.That((sourceText.RenderTransform as TranslateTransform)?.Y, Is.EqualTo(0));
                    Assert.That(galleryIcons.Select(i => i.Size), Is.All.EqualTo(size.Value));
                    window.MouseUp(end, MouseButton.Left);
                    Pump();
                    thumb.Focus();
                    Press(Key.Home);
                    Assert.That(size.Value, Is.EqualTo(16));
                    Press(Key.Right);
                    Assert.That(size.Value, Is.EqualTo(17), "Arrow keys adjust one DIP without navigating the icon grid.");
                    AssertStablePreview();
                    Assert.That(Find<TextBlock>("SizeValue").Text, Is.EqualTo("17 DIP"));
                    Assert.That(code.Code, Does.Contain("Size=\"17\""));
                    Press(Key.End);
                    Assert.That(size.Value, Is.EqualTo(64));
                    Assert.That(galleryIcons.Select(i => i.Size), Is.All.EqualTo(64));
                    Assert.That(code.Code, Does.Contain("Size=\"64\""));
                    AssertStablePreview();
                    Assert.That(library.Items.Cast<StudioRow>().SelectMany(r => r.Icons), Is.EqualTo(entries), "Size changes retain entry identity.");
                    Assert.That(inspector.FindControl<KubaiIcon>("Specimen"), Is.SameAs(icon));
                    window.MouseMove(new Point(2, 2));
                    tile.Focus();
                    Pump();
                    Capture(window, "studio-size-slider");

                    Click(captions.Single(b => b.Name == Arc.WindowCaptionButtons.CloseButtonPart));
                    Assert.That(window.IsVisible, Is.False);
                    Assert.That(icon.IsAnimating, Is.False);
                    return true;
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}