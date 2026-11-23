using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using IconStudio;
using KubaiIcons.Avalonia;
using NUnit.Framework;
using TKCode.Arc.Motion;
using Arc = TKCode.Arc;
using static TKCode.Arc.ArcThemeBuilderExtensions;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test]
    public async Task Studio_WhenResizingChartIcons_KeepsWrappedCaptionsInsideTheirTilesAndOnScreen()
    {
        await _session.Dispatch(
            () =>
            {
                var window = new StudioWindow
                {
                    Width = 1440,
                    Height = 960
                };
                try
                {
                    window.FindControl<Arc.Sidebar>("Categories")!.SelectedKey = "charts";
                    window.Show();
                    Pump();
                    var library = window.FindControl<ListBox>("Library")!;
                    var size = window.FindControl<Arc.Slider>("IconSize")!;
                    var tile = library.GetVisualDescendants()
                        .OfType<Button>()
                        .Single(b => b.DataContext is StudioEntry { Name: "chart-candlestick-plus" });
                    var caption = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "chart-candlestick-plus");
                    var glyph = tile.GetVisualDescendants().OfType<KubaiIcon>().Single();
                    Assert.That(caption.TextLayout.TextLines.Count, Is.EqualTo(2));
                    var origin = caption.TranslatePoint(default, tile) ?? throw new AssertionException("Detached caption.");
                    Assert.That(origin.X, Is.GreaterThanOrEqualTo(0));
                    Assert.That(origin.X + caption.Bounds.Width, Is.LessThanOrEqualTo(tile.Bounds.Width));
                    Assert.That(origin.Y + caption.Bounds.Height, Is.LessThanOrEqualTo(tile.Bounds.Height));
                    var first = caption.TranslatePoint(default, window) ?? throw new AssertionException("Detached tile.");
                    var captionRect = PixelRect.FromRect(new Rect(first, caption.Bounds.Size), window.RenderScaling);
                    var baseline = ReadRegion(window, captionRect);
                    for (var line = 0; line < 2; line++)
                    {
                        var half = baseline.Length / 2;
                        Assert.That(
                            Enumerable.Range(line * half, half).Where(i => i % 4 == 0).Select(i => baseline[i]).Distinct().Count(),
                            Is.GreaterThan(5),
                            $"Caption line {line + 1} must contain visible text.");
                    }

                    foreach (var value in new[] { 16, 37, 64, 32 })
                    {
                        size.Value = value;
                        window.UpdateLayout();
                        Assert.That(glyph.Size, Is.EqualTo(value));
                        Assert.That(caption.TranslatePoint(default, window), Is.EqualTo(first));
                        Assert.That(
                            ReadRegion(window, captionRect),
                            Is.EqualTo(baseline),
                            $"The label changed while resizing the glyph to {value} DIP.");
                        if (value == 37)
                        {
                            Capture(window, "studio-chart-captions");
                        }
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static byte[] ReadRegion(Window window, PixelRect rect)
    {
        using var shot = window.CaptureRenderedFrame() ?? throw new AssertionException("No Studio frame.");
        var pixels = new byte[rect.Width * rect.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            shot.CopyPixels(rect, handle.AddrOfPinnedObject(), pixels.Length, rect.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    [Test, Order(6)]
    public async Task Studio_WhenHoveringAndScrolling_ShowsOpaqueHintsAndKeepsCodeActionsVisible()
    {
        await _session.Dispatch(
            async () =>
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

                Arc.Tooltip Bubble(object? content)
                {
                    return window.GetVisualDescendants()
                    .OfType<Arc.Tooltip>()
                    .Single(t => t.IsEffectivelyVisible && Equals(t.Content, content));
                }

                async Task<Arc.Tooltip> Hover(Control control)
                {
                    Arc.Tooltip.SetShowDelay(control, TimeSpan.Zero);
                    window.MouseMove(new Point(2, 2));
                    window.MouseMove(Center(control));
                    await Task.Delay(80);
                    Pump();
                    return Bubble(Arc.Tooltip.GetTip(control));
                }

                try
                {
                    Find<TextBox>("Search").Text = "award";
                    window.Show();
                    Pump();
                    var tile = Find<ListBox>("Library")
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .Single(b => b.DataContext is StudioEntry { Name: "award" });
                    Assert.That(ToolTip.GetTip(tile), Is.Null, "An unthemed Avalonia ToolTip must not be used in the Arc host.");
                    var bubble = await Hover(tile);
                    var darkBackground = bubble.Background as ISolidColorBrush;
                    Assert.That(darkBackground?.Color.A, Is.EqualTo(255));
                    Assert.That(bubble.GetVisualDescendants().OfType<Border>().Any(b => Equals(b.Background, bubble.Background)), Is.True);
                    Capture(window, "studio-tooltip-dark");

                    window.MouseMove(new Point(2, 2));
                    Pump();
                    Arc.ArcTheme.Apply(t => t.Appearance(Arc.ArcAppearance.Light));
                    Pump();
                    bubble = await Hover(tile);
                    Assert.That((bubble.Background as ISolidColorBrush)?.Color.A, Is.EqualTo(255));
                    Assert.That(bubble.Background, Is.Not.EqualTo(darkBackground));
                    Capture(window, "studio-tooltip-light");
                    window.MouseMove(new Point(2, 2));
                    Pump();
                    Arc.ArcTheme.Apply(t => t.Appearance(Arc.ArcAppearance.Dark));
                    Pump();

                    Click(tile);
                    var inspector = Find<ContentControl>("DetailHost").Content as IconInspector ?? throw new AssertionException("No inspector.");
                    T Detail<T>(string name) where T : Control => inspector.FindControl<T>(name) ?? throw new AssertionException(name);
                    var active = Detail<Arc.Switch>("Active");
                    Assert.That(active.IsEnabled, Is.False);
                    static bool HasTip(Control control) => Arc.Tooltip.GetTip(control) is not null || ToolTip.GetTip(control) is not null;
                    var toolbar = Find<Arc.SegmentedControl>("Family").Parent as Grid ?? throw new AssertionException("No gallery toolbar.");
                    Assert.That(toolbar.GetVisualDescendants().OfType<Control>().Where(HasTip), Is.Empty);
                    Assert.That(inspector.GetVisualDescendants().OfType<Control>().Where(HasTip), Is.EqualTo(new[] { active }));
                    Assert.That(Arc.Tooltip.GetShowOnDisabled(active), Is.True);
                    bubble = await Hover(active);
                    Assert.That(bubble.Content, Is.EqualTo("This artwork has no active-state variation."));
                    Assert.That((bubble.Background as ISolidColorBrush)?.Color.A, Is.EqualTo(255));
                    Capture(window, "studio-disabled-tooltip");
                    window.MouseMove(new Point(2, 2));
                    Pump();

                    Detail<Arc.SegmentedControl>("CodeLanguage").SelectedIndex = 1;
                    Pump();
                    var frame = Detail<Border>("CodeFrame");
                    var header = Detail<Border>("CodeHeader");
                    var source = Detail<Arc.CodeBlock>("Snippet");
                    var copy = Detail<Arc.CopyButton>("CodeCopy");
                    var scroll = Detail<ScrollViewer>("CodeScroll");
                    var headerPosition = header.TranslatePoint(default, window);
                    var copyPosition = copy.TranslatePoint(default, window);
                    var codePosition = source.TranslatePoint(default, window) ?? throw new AssertionException("No code bounds.");
                    Assert.That(Detail<TextBlock>("CodeFileName").Text, Is.EqualTo("Icon.cs"));
                    var language = Detail<Arc.SegmentedControl>("CodeLanguage");
                    Assert.That(language.TranslatePoint(default, window)?.X, Is.EqualTo(frame.TranslatePoint(default, window)?.X));
                    var languageBottom = language.TranslatePoint(new Point(0, language.Bounds.Height), inspector)?.Y;
                    var frameTop = frame.TranslatePoint(default, inspector)?.Y;
                    Assert.That(frameTop - languageBottom, Is.EqualTo(8).Within(1));
                    var surface = Detail<Border>("DrawerSurface");
                    var frameEnd = frame.TranslatePoint(new Point(frame.Bounds.Width, frame.Bounds.Height), surface)
                        ?? throw new AssertionException("Detached code frame.");
                    var inset = surface.CornerRadius.BottomRight - frame.CornerRadius.BottomRight;
                    Assert.That(surface.Bounds.Width - frameEnd.X, Is.EqualTo(inset).Within(1));
                    Assert.That(surface.Bounds.Height - frameEnd.Y, Is.EqualTo(inset).Within(1));
                    Assert.That(copy.IsIconOnly, Is.True);
                    Assert.That(copy.Variant, Is.EqualTo(Arc.CopyButtonVariant.Plain));
                    Assert.That(Arc.Tooltip.GetTip(copy), Is.Null);
                    Assert.That(source.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Header").IsVisible, Is.False);
                    Assert.That(frame.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "·"), Is.False);
                    Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height));

                    // Scroll over the source itself: only the body may move, never the file/language/copy toolbar.
                    window.MouseMove(Center(scroll));
                    window.MouseWheel(Center(scroll), new Vector(0, -4));
                    Pump();
                    Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
                    Assert.That(header.TranslatePoint(default, window), Is.EqualTo(headerPosition));
                    Assert.That(copy.TranslatePoint(default, window), Is.EqualTo(copyPosition));
                    Assert.That(source.TranslatePoint(default, window)?.Y, Is.LessThan(codePosition.Y));
                    var offset = scroll.Offset;
                    var lines = source.LineCount;
                    Find<Arc.Slider>("IconSize").Value = 24;
                    Pump();
                    Assert.That(source.Code, Does.Contain("Size = 24"));
                    Assert.That(source.LineCount, Is.EqualTo(lines));
                    Assert.That(scroll.Offset, Is.EqualTo(offset), "Crossing 24 DIP must not move scrolled source.");
                    Capture(window, "studio-code-scrolled");
                    Click(copy);
                    Assert.That(await (window.Clipboard ?? throw new AssertionException("No clipboard.")).TryGetTextAsync(), Is.EqualTo(source.Code));
                    Assert.That(copy.State, Is.EqualTo(Arc.CopyState.Copied));
                    Assert.That(Arc.Tooltip.GetTip(copy), Is.Null);

                    // Frequent palette actions must remain visible and non-overlapping at the minimum window size.
                    var swatches = Find<ItemsControl>("Swatches").GetVisualDescendants().OfType<Button>().ToArray();
                    Assert.That(swatches.Length, Is.EqualTo(5));
                    Assert.That(swatches.All(b => b.IsEffectivelyVisible && b.Bounds.Width >= 32), Is.True);
                    var custom = Find<Arc.ColorPicker>("CustomColor");
                    var size = Find<Arc.Slider>("IconSize");
                    Assert.That(
                        custom.TranslatePoint(new Point(custom.Bounds.Width, 0), window)?.X,
                        Is.LessThan((size.TranslatePoint(default, window) ?? throw new AssertionException("No size control bounds.")).X));
                    var mixed = Find<Button>("Mixed");
                    var disc = mixed.GetVisualDescendants().OfType<Ellipse>().Single();
                    var gradient = disc.Fill as ConicGradientBrush ?? throw new AssertionException("Mixed must use a continuous color wheel.");
                    Assert.That(
                        gradient.GradientStops[0].Color,
                        Is.EqualTo(gradient.GradientStops[^1].Color),
                        "The color wheel must join without a seam.");
                    Assert.That(gradient.GradientStops.Select(s => s.Offset).Distinct().Count(), Is.EqualTo(gradient.GradientStops.Count));
                    Assert.That(mixed.Bounds.Size, Is.EqualTo(swatches[0].Bounds.Size));
                    Assert.That(disc.Bounds.Size, Is.EqualTo(swatches[0].GetVisualDescendants().OfType<Ellipse>().Single().Bounds.Size));
                    Assert.That(mixed.Classes.Contains("selected"), Is.True);
                    var entry = tile.DataContext as StudioEntry ?? throw new AssertionException("No icon entry.");
                    var stableColor = entry.MixedColor;
                    Click(swatches[0]);
                    Assert.That(swatches[0].Classes.Contains("selected"), Is.True);
                    Assert.That(mixed.Classes.Contains("selected"), Is.False);
                    Assert.That(source.Code, Does.Contain(StudioCatalog.Swatches[0].Hex));
                    Click(mixed);
                    Assert.That(mixed.Classes.Contains("selected"), Is.True);
                    Assert.That(swatches.Any(b => b.Classes.Contains("selected")), Is.False);
                    Assert.That(entry.Color, Is.EqualTo(stableColor));
                    Capture(window, "studio-mixed-palette");
                    return true;
                }
                finally
                {
                    window.Close();
                    Arc.ArcTheme.Apply(t => t.Appearance(Arc.ArcAppearance.Dark).Accent(Arc.ArcAccentPreset.Neutral));
                }
            },
            CancellationToken.None);
    }
}