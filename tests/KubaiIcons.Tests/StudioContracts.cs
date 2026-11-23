using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Styling;
using IconStudio;
using KubaiIcons.Avalonia;
using NUnit.Framework;
using TKCode.Arc.Motion;
using Arc = TKCode.Arc;
using static TKCode.Arc.ArcThemeBuilderExtensions;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test, Order(5)]
    public async Task Studio_WhenBrowsingAndCopying_VirtualizesTilesAndKeepsPreviewCodeAndColorInSync()
    {
        await _session.Dispatch(
            async () =>
            {
                using var reads = new DecodeListener();
                var catalog = new StudioCatalog();
                Assert.That(catalog.Categories.Count, Is.EqualTo(26));
                Assert.That(catalog.Icons.Count, Is.EqualTo(3074));
                Assert.That(catalog.Icons.Count(i => i.SearchScore(["calculate"]) >= 0), Is.GreaterThan(0));
                Assert.That(reads.Names, Is.Empty, "Loading and searching Studio metadata must not decode artwork.");
                var window = new StudioWindow();
                T Find<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new AssertionException(name);
                var search = Find<TextBox>("Search");
                var grid = Find<ListBox>("Library");
                var host = Find<ContentControl>("DetailHost");
                var family = Find<Arc.SegmentedControl>("Family");
                var palette = Find<Arc.SegmentedControl>("PaletteMode");
                StudioEntry[] Results() => grid.Items.Cast<StudioRow>().SelectMany(r => r.Icons).ToArray();

                Button Tile(string name)
                {
                    return grid.GetVisualDescendants()
                    .OfType<Button>()
                    .First(b => b.DataContext is StudioEntry i && i.Name == name);
                }

                void Click(Control control)
                {
                    var topLevel = TopLevel.GetTopLevel(control) ?? throw new AssertionException("Detached control.");
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)
                        ?? throw new AssertionException("No control bounds.");
                    topLevel.MouseMove(point);
                    topLevel.MouseDown(point, MouseButton.Left);
                    topLevel.MouseUp(point, MouseButton.Left);
                    Pump();
                }

                try
                {
                    window.Show();
                    Pump();
                    Assert.That(host.Content, Is.Null, "The inspector is not constructed before selection.");
                    Assert.That(Results().Length, Is.EqualTo(3071));
                    Assert.That(family.SelectedIndex, Is.Zero);
                    Assert.That(Results().Select(i => i.Style), Is.All.EqualTo(KubaiIconStyle.Layered));
                    Assert.That(palette.IsEnabled && palette.SelectedIndex == 0, Is.True);
                    Assert.That(grid.GetRealizedContainers().Count(), Is.InRange(1, 20));
                    Assert.That(grid.GetVisualDescendants().OfType<KubaiIcon>().Count(), Is.InRange(1, 150));
                    Assert.That(reads.Names.Count, Is.InRange(1, 150), "Only visible/buffered tiles decode, not the full catalog.");
                    var scroll = grid.GetVisualDescendants().OfType<ScrollViewer>().Single();
                    var bar = scroll.GetVisualDescendants()
                        .OfType<ScrollBar>()
                        .Single(b => b.Orientation == Orientation.Vertical);

                    Rect BoundsInWindow(Control control)
                    {
                        return new(
                        control.TranslatePoint(default, window)
                        ?? throw new AssertionException("Detached control."),
                        control.Bounds.Size);
                    }

                    Assert.That(BoundsInWindow(scroll).Bottom, Is.EqualTo(window.ClientSize.Height).Within(1));
                    var rightmost = grid.GetVisualDescendants()
                        .OfType<Button>()
                        .Where(b => b.Classes.Contains("tile"))
                        .Max(b => BoundsInWindow(b).Right);
                    Assert.That(BoundsInWindow(bar).Left, Is.GreaterThan(rightmost), "The scroll bar's hit area belongs in the gutter.");
                    window.MouseMove(BoundsInWindow(bar).Center);
                    Pump();
                    Assert.That(bar.IsHitTestVisible, Is.True);
                    Capture(window, "studio-gallery-edges");
                    window.MouseMove(new Point(2, 2));
                    Pump();
                    Capture(window, "studio-grid");
                    family.SelectedIndex = 1;
                    Pump();
                    Assert.That(Results().Length, Is.EqualTo(502));
                    Assert.That(palette.IsEnabled, Is.False);
                    Find<Arc.Slider>("IconSize").Value = 41;
                    grid.ScrollIntoView(grid.ItemCount - 1);
                    Pump();
                    var lastGlyph = Tile("zoom-out").GetVisualDescendants().OfType<KubaiIcon>().Single();
                    Assert.That(lastGlyph.Size, Is.EqualTo(41), "Newly realized tiles use the current slider value.");
                    Assert.That(grid.GetVisualDescendants().OfType<KubaiIcon>().Count(), Is.LessThan(150));
                    Assert.That(grid.GetVisualDescendants().OfType<Button>().Any(b => b.DataContext is StudioEntry { Name: "zoom-out" }), Is.True);

                    var lastTile = BoundsInWindow(Tile("zoom-out"));
                    Assert.That(lastTile.Bottom, Is.LessThanOrEqualTo(BoundsInWindow(scroll).Bottom));
                    var shortcut = Find<Border>("SearchShortcut");
                    Assert.That(shortcut.IsEffectivelyVisible && !shortcut.IsHitTestVisible, Is.True);
                    Click(shortcut);
                    Assert.That(search.IsFocused, Is.True);
                    Assert.That(shortcut.IsVisible, Is.False);
                    window.KeyTextInput("calendar");
                    Pump();
                    Assert.That(Results().Select(i => i.Name), Does.Contain("calendar"));
                    var calendar = Results().Single(i => i.Name == "calendar");
                    var color = calendar.Color;
                    Tile("calendar").Focus(NavigationMethod.Tab);
                    Assert.That(Tile("calendar").GetVisualDescendants().OfType<KubaiIcon>().Single().IsEngaged, Is.True);
                    window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                    window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                    Pump();
                    Assert.That(window.FocusManager.GetFocusedElement(), Is.SameAs(Tile("calendar-check")));
                    Assert.That(shortcut.IsVisible, Is.False, "A filled field reserves its suffix for Clear.");
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Pump();
                    Assert.That((host.Content as IconInspector)?.FindControl<KubaiIcon>("Specimen")?.Kind, Is.EqualTo(KubaiIconKind.CalendarCheck));
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Pump();
                    Assert.That(host.Content, Is.Null);
                    var calendarIcon = Tile("calendar").GetVisualDescendants().OfType<KubaiIcon>().Single();
                    var tapPoint = BoundsInWindow(calendarIcon).Center;
                    using (var touch = window.TouchBegin(tapPoint))
                    {
                        window.TouchEnd(touch, tapPoint);
                    }
                    Pump();
                    Assert.That(PhaseOf(calendarIcon), Is.EqualTo("Outbound"));
                    var inspector = host.Content as IconInspector ?? throw new AssertionException("No detail.");
                    T Detail<T>(string name) where T : Control => inspector.FindControl<T>(name) ?? throw new AssertionException(name);
                    var icon = Detail<KubaiIcon>("Specimen");
                    Assert.That(icon.Kind, Is.EqualTo(KubaiIconKind.Calendar));
                    var headerBounds = BoundsInWindow(Find<Panel>("SearchHeader"));
                    Assert.That(headerBounds.Top, Is.EqualTo(BoundsInWindow(scroll).Top).Within(1));
                    Assert.That(headerBounds.Left, Is.EqualTo(BoundsInWindow(scroll).Left).Within(1));
                    Assert.That(headerBounds.Right, Is.EqualTo(BoundsInWindow(scroll).Right).Within(1));
                    Assert.That(BoundsInWindow(bar).Top, Is.GreaterThanOrEqualTo(headerBounds.Bottom - 1));
                    Assert.That(
                        BoundsInWindow(scroll).Bottom,
                        Is.EqualTo(window.ClientSize.Height).Within(1),
                        "Opening the inspector must not shorten the gallery behind its sides or bottom.");
                    Assert.That(BoundsInWindow(Tile("calendar")).Top, Is.GreaterThanOrEqualTo(headerBounds.Bottom));
                    Assert.That(icon.AccentColor, Is.EqualTo(color));
                    Assert.That(Detail<Arc.Switch>("Active").IsVisible && palette.IsVisible, Is.True);
                    Assert.That(Detail<Arc.Switch>("Active").IsEnabled || palette.IsEnabled, Is.False);
                    Click(Detail<Arc.Switch>("Active"));
                    Assert.That(icon.IsActive, Is.False, "An unsupported visible switch must not change state.");
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Contain("Kind=\"Calendar\""));
                    Capture(window, "studio-inspector");
                    search.Text = "";
                    Pump();
                    Assert.That(host.Content, Is.SameAs(inspector), "Clearing a filter preserves the selected inspector.");
                    Capture(window, "studio-overview");

                    // Scrolling artwork shows through the search header but never through the inspector.
                    byte[] BackdropPixels(Control surface)
                    {
                        var box = BoundsInWindow(surface);
                        var patch = new Rect(box.X + box.Width * 0.65, box.Y + 12, box.Width * 0.25, 36);
                        return ReadRegion(window, PixelRect.FromRect(patch, window.RenderScaling));
                    }

                    int BackdropRange(Control surface)
                    {
                        var pixels = BackdropPixels(surface);
                        var minimum = byte.MaxValue;
                        var maximum = byte.MinValue;
                        for (var i = 0; i < pixels.Length; i += 4)
                        {
                            minimum = Math.Min(minimum, pixels[i]);
                            maximum = Math.Max(maximum, pixels[i]);
                        }

                        return maximum - minimum;
                    }

                    var probeGlyph = grid.GetVisualDescendants()
                        .OfType<KubaiIcon>()
                        .Select(BoundsInWindow)
                        .First(box => box.Center.X > headerBounds.X + headerBounds.Width * .7 &&
                            box.Center.X < headerBounds.X + headerBounds.Width * .85);
                    scroll.Offset = new Vector(0, probeGlyph.Center.Y - headerBounds.Top - 30);
                    Pump();
                    var searchHeader = Find<Panel>("SearchHeader");
                    Assert.That(BoundsInWindow(searchHeader), Is.EqualTo(headerBounds), "The header stays docked while the gallery scrolls.");
                    Assert.That(BackdropRange(searchHeader), Is.GreaterThan(4));
                    Assert.That(BackdropRange(inspector), Is.Zero, "The floating card must stay opaque over artwork.");
                    var firstRows = BackdropPixels(searchHeader);
                    scroll.Offset += new Vector(0, 36 * StudioRow.Height);
                    Pump();
                    Assert.That(BackdropRange(searchHeader), Is.GreaterThan(4), "Recycled rows must still show through the header.");
                    var farRows = BackdropPixels(searchHeader);
                    Assert.That(farRows, Is.Not.EqualTo(firstRows));
                    Click(Find<ItemsControl>("Swatches").GetVisualDescendants().OfType<Button>().First());
                    Assert.That(BackdropPixels(searchHeader), Is.Not.EqualTo(farRows), "A palette change refreshes the frost even at rest.");
                    Click(Find<Button>("Mixed"));
                    Capture(window, "studio-docked-glass");
                    window.Resources[Arc.DesignTokens.ArcDesignTokenKind.ReduceTransparency] = true;
                    Pump();
                    Assert.That(BackdropRange(searchHeader), Is.Zero);
                    Assert.That(BackdropRange(inspector), Is.Zero);
                    window.Resources.Remove(Arc.DesignTokens.ArcDesignTokenKind.ReduceTransparency);
                    scroll.ScrollToHome();
                    Pump();
                    Tile("activity").Focus();
                    window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                    window.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                    Pump();
                    Assert.That(window.FocusManager.GetFocusedElement(), Is.SameAs(Tile("zoom-out")));
                    Assert.That(
                        BoundsInWindow(Tile("zoom-out")).Bottom,
                        Is.LessThanOrEqualTo(BoundsInWindow(inspector).Top + 1),
                        "End must reveal the full focused tile above the inspector.");
                    scroll.ScrollToEnd();
                    Pump();
                    Assert.That(
                        BoundsInWindow(Tile("zoom-out")).Bottom,
                        Is.LessThanOrEqualTo(BoundsInWindow(inspector).Top + 1),
                        "At the end of the gallery the overlay must not hide the last tile.");
                    window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    Pump();
                    Assert.That(BoundsInWindow(Tile("activity")).Top, Is.GreaterThanOrEqualTo(headerBounds.Bottom));
                    search.Text = "calendar";
                    Pump();

                    // Escape belongs to an open Arc choice before it closes the surrounding inspector.
                    Click(Detail<Button>("MotionOptions"));
                    Assert.That(Detail<Arc.Popover>("MotionSettings").IsOpen, Is.True);
                    var loopSelect = Detail<Arc.Select>("Loops");
                    loopSelect.Focus();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Pump();
                    Assert.That(loopSelect.IsOpen, Is.True);
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Pump();
                    Assert.That(loopSelect.IsOpen, Is.False);
                    Assert.That(Detail<Arc.Popover>("MotionSettings").IsOpen, Is.True);
                    Assert.That(host.Content, Is.SameAs(inspector));
                    Capture(window, "studio-animation-settings");
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Pump();
                    Assert.That(Detail<Arc.Popover>("MotionSettings").IsOpen, Is.False);
                    Assert.That(host.Content, Is.SameAs(inspector));
                    Assert.That(window.FocusManager.GetFocusedElement(), Is.SameAs(Detail<Button>("MotionOptions")));

                    // Hover is preview feedback, not a request exported as IsEngaged=True.
                    Click(Detail<Button>("Stage"));
                    Assert.That(icon.IsEngaged, Is.True);
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Not.Contain("IsEngaged"));
                    Click(Detail<Arc.CopyButton>("CodeCopy"));
                    var clipboard = window.Clipboard ?? throw new AssertionException("No clipboard.");
                    Assert.That(await clipboard.TryGetTextAsync(), Is.EqualTo(Detail<Arc.CodeBlock>("Snippet").Code));
                    Assert.That(Detail<Arc.CopyButton>("CodeCopy").State, Is.EqualTo(Arc.CopyState.Copied));
                    Detail<Arc.SegmentedControl>("CodeLanguage").SelectedIndex = 1;
                    Pump();
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Contain("KubaiDuotoneIconKind.Calendar"));
                    Click(Detail<Arc.CopyButton>("CodeCopy"));
                    Assert.That(await clipboard.TryGetTextAsync(), Is.EqualTo(Detail<Arc.CodeBlock>("Snippet").Code));

                    Assert.That(Find<ItemsControl>("Swatches").GetVisualDescendants().OfType<Button>().Count(), Is.EqualTo(5));
                    var custom = Find<Arc.ColorPicker>("CustomColor");
                    Assert.That(
                        custom.ContrastBackground,
                        Is.Not.EqualTo(Colors.White),
                        "Use the dark gallery surface for the picker’s contrast preview.");
                    Click(custom);
                    Assert.That(custom.IsDropDownOpen && custom.Classes.Contains("selected"), Is.True);
                    Assert.That(icon.AccentColor, Is.EqualTo(custom.Color));
                    var input = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_Input" && t.IsEffectivelyVisible);
                    input.Focus();
                    input.SelectAll();
                    window.KeyTextInput("#E879F9");
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Pump();
                    Assert.That(custom.Color, Is.EqualTo(Color.Parse("#E879F9")));
                    Assert.That(icon.AccentColor, Is.EqualTo(custom.Color));
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Contain("#E879F9"));
                    Capture(window, "studio-color-picker");
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Pump();
                    Assert.That(custom.IsDropDownOpen, Is.False);
                    Assert.That(host.Content, Is.SameAs(inspector));
                    Click(Find<ItemsControl>("Swatches").GetVisualDescendants().OfType<Button>().First());
                    Assert.That(custom.Classes.Contains("selected"), Is.False);
                    Click(custom);
                    Assert.That(icon.AccentColor, Is.EqualTo(Color.Parse("#E879F9")));
                    custom.IsDropDownOpen = false;
                    Pump();
                    Click(Find<Button>("Mixed"));
                    Assert.That(calendar.Color, Is.EqualTo(color));
                    Find<Arc.Slider>("IconSize").Value = 48;
                    Pump();
                    Assert.That(Tile("calendar").GetVisualDescendants().OfType<KubaiIcon>().Single().Size, Is.EqualTo(48));
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Contain("Size = 48"));

                    Click(Tile("calendar-check"));
                    Assert.That(host.Content, Is.SameAs(inspector));
                    Assert.That(icon.Kind, Is.EqualTo(KubaiIconKind.CalendarCheck));
                    window.Width = 1100;
                    window.Height = 740;
                    Pump();
                    Assert.That(calendar.Color, Is.EqualTo(color));
                    Assert.That(
                        BoundsInWindow(inspector).Top - BoundsInWindow(Find<Panel>("SearchHeader")).Bottom,
                        Is.GreaterThanOrEqualTo(StudioRow.Height));
                    Capture(window, "studio-compact");
                    window.Width = 1440;
                    window.Height = 960;
                    Pump();

                    search.Text = "相机";
                    Pump();
                    Assert.That(Results().Any(i => i.Kind == KubaiIconKind.Camera), Is.True);
                    search.Text = "no-such-icon";
                    Pump();
                    Assert.That(Results(), Is.Empty);
                    Assert.That(Find<Arc.EmptyState>("NoResults").IsVisible, Is.True);
                    Assert.That(host.Content, Is.SameAs(inspector), "An empty search preserves the current detail.");
                    search.Text = "";
                    var categories = Find<Arc.Sidebar>("Categories");
                    categories.SelectedKey = "files";
                    Pump();
                    Assert.That(Results().Length, Is.EqualTo(25));
                    Assert.That(Results().All(i => i.Category == "files"), Is.True);
                    Assert.That(new StudioCatalog().Icons.Single(i => i.Name == "calendar").MixedColor, Is.EqualTo(color));

                    categories.SelectedKey = "";
                    family.SelectedIndex = 0;
                    Pump();
                    Assert.That(Results().Length, Is.EqualTo(3071));
                    Assert.That(host.Content, Is.SameAs(inspector), "A shared icon keeps its inspector across families.");
                    search.Text = "bookmark";
                    Pump();
                    Click(Tile("bookmark"));
                    inspector = host.Content as IconInspector ?? throw new AssertionException("No layered detail.");
                    icon = Detail<KubaiIcon>("Specimen");
                    Assert.That(Detail<Arc.Switch>("Active").IsEnabled && palette.IsEnabled, Is.True);
                    Assert.That(Arc.Tooltip.GetTip(Detail<Arc.Switch>("Active")), Is.Null);
                    Detail<Arc.Switch>("Animate").IsChecked = false;
                    Detail<Arc.Switch>("Hold").IsChecked = true;
                    Detail<Arc.Switch>("Active").IsChecked = true;
                    palette.SelectedIndex = 1;
                    Pump();
                    Assert.That(grid.GetVisualDescendants().OfType<KubaiIcon>().Select(i => i.ColorVariant), Is.All.EqualTo(KubaiColorVariant.Soft));
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Contain("KubaiColorVariant.Soft"));
                    family.SelectedIndex = 1;
                    Pump();
                    Assert.That(palette.IsEnabled || palette.SelectedIndex != 0, Is.False);
                    Assert.That(icon.ColorVariant, Is.EqualTo(KubaiColorVariant.Color));
                    family.SelectedIndex = 0;
                    Pump();
                    Assert.That(palette.IsEnabled && palette.SelectedIndex == 1, Is.True);
                    Assert.That(icon.IsEngaged && icon.IsActive, Is.True);
                    Assert.That(icon.ColorVariant, Is.EqualTo(KubaiColorVariant.Soft));
                    Detail<Arc.Select>("Loops").SelectedIndex = 1;
                    Detail<Arc.Select>("Speeds").SelectedIndex = 2;
                    Detail<Arc.Select>("Gaps").SelectedIndex = 1;
                    Detail<Arc.Switch>("Playing").IsChecked = true;
                    Assert.That(icon.LoopMode, Is.EqualTo(LoopMode.Repeat));
                    Assert.That(icon.SpeedRatio, Is.EqualTo(2));
                    Assert.That(icon.RepeatDelay, Is.EqualTo(TimeSpan.FromMilliseconds(300)));
                    Assert.That(icon.IsPlaying, Is.True);
                    Detail<Arc.Switch>("Animate").IsChecked = true;
                    if (SystemAllowsMotion())
                    {
                        Step(icon, 260);
                        Step(icon, 260);
                        Assert.That(PhaseOf(icon), Is.EqualTo("Waiting"));
                        Find<Arc.Slider>("IconSize").Value = 47;
                        Assert.That(PhaseOf(icon), Is.EqualTo("Waiting"), "A size adjustment must not retrigger playback.");
                    }

                    Detail<Arc.SegmentedControl>("CodeLanguage").SelectedIndex = 1;
                    Assert.That(Detail<Arc.CodeBlock>("Snippet").Code, Does.Contain("IsActive = true").And.Contain("TimeSpan.FromMilliseconds(300)"));
                    // Capability switches stay in the same layout slot across artwork families.
                    var activeBounds = Detail<Arc.Switch>("Active").Bounds;
                    search.Text = "folder";
                    Pump();
                    Click(Tile("folder"));
                    Assert.That(Detail<Arc.Switch>("Active").IsEnabled, Is.False);
                    Assert.That(icon.IsActive, Is.False);
                    Assert.That(Detail<Arc.Switch>("Active").Bounds, Is.EqualTo(activeBounds));
                    search.Text = "bookmark";
                    Pump();
                    Click(Tile("bookmark"));
                    Assert.That(icon.IsActive, Is.True, "A disabled capability preserves the user's configured preference.");
                    var activeBottom = Detail<Arc.Switch>("Active").TranslatePoint(new Point(0, activeBounds.Height), inspector)
                        ?? throw new AssertionException("No state-switch bounds.");
                    Assert.That(activeBottom.Y, Is.LessThanOrEqualTo(inspector.Bounds.Height));
                    Capture(window, "studio-layered");
                    ArcMotion.SetLevel(window, ArcMotionLevel.Off);
                    var themeBackground = window.Background;
                    // Arc prepares and publishes its theme asynchronously.
                    var changedTheme = new TaskCompletionSource();

                    void OnThemeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                    {
                        if (e.Property == TopLevel.ActualThemeVariantProperty && window.ActualThemeVariant == ThemeVariant.Light)
                        {
                            changedTheme.TrySetResult();
                        }
                    }

                    window.PropertyChanged += OnThemeChanged;
                    try
                    {
                        Click(Find<Arc.ThemeSwitcher>("ThemeToggle"));
                        await changedTheme.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    finally
                    {
                        window.PropertyChanged -= OnThemeChanged;
                    }

                    Pump();
                    Assert.That(window.ActualThemeVariant, Is.EqualTo(ThemeVariant.Light));
                    Assert.That(window.Background, Is.Not.EqualTo(themeBackground));
                    window.MouseMove(new Point(2, 2));
                    Pump();
                    var themeButton = Find<Arc.ThemeSwitcher>("ThemeToggle").GetVisualDescendants().OfType<Arc.Button>().Single();
                    Assert.That(themeButton.BorderThickness, Is.EqualTo(new Thickness(0)));
                    Assert.That((themeButton.Background as ISolidColorBrush)?.Color.A, Is.EqualTo(0));
                    Assert.That(icon.AccentColor, Is.EqualTo(StudioCatalog.Swatches[4].Color));
                    Capture(window, "studio-light");
                    Arc.ArcTheme.Apply(t => t.Accent(Arc.ArcAccentPreset.Violet));
                    Pump();
                    Capture(window, "studio-light-violet");
                    Assert.That(icon.IsActive && icon.IsEngaged, Is.True);
                    Arc.ArcTheme.Apply(t => t.Appearance(Arc.ArcAppearance.Dark).Accent(Arc.ArcAccentPreset.Neutral));
                    Pump();
                    Assert.That(window.ActualThemeVariant, Is.EqualTo(ThemeVariant.Dark));
                    ArcMotion.SetLevel(window, ArcMotionLevel.Full);
                    var output = Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR");
                    if (output is not null)
                    {
                        File.WriteAllText(Path.Combine(output, "Usage.cs"), Detail<Arc.CodeBlock>("Snippet").Code);
                        Detail<Arc.SegmentedControl>("CodeLanguage").SelectedIndex = 0;
                        File.WriteAllText(Path.Combine(output, "Usage.axaml"), Detail<Arc.CodeBlock>("Snippet").Code);
                    }

                    Click(Detail<Button>("StopPlayback"));
                    Assert.That(icon.IsPlaying, Is.True, "Stop() must not write the checkbox request.");
                    Click(Detail<Button>("CloseDetail"));
                    Assert.That(host.Content, Is.Null);
                    Assert.That(icon.IsAnimating, Is.False);
                    search.Text = "folder";
                    Pump();
                    Tile("folder").Focus();
                    window.KeyPress(Key.OemQuestion, RawInputModifiers.None, PhysicalKey.Slash, "/");
                    window.KeyRelease(Key.OemQuestion, RawInputModifiers.None, PhysicalKey.Slash, "/");
                    Pump();
                    Assert.That(window.FocusManager.GetFocusedElement(), Is.SameAs(search));
                    Assert.That(TopLevel.GetTopLevel(icon), Is.Null);
                    Assert.That(BoundsInWindow(scroll).Bottom, Is.EqualTo(window.ClientSize.Height).Within(1));
                    search.Text = "";
                    Find<Button>("AboutTrigger").Focus();
                    Pump();
                    Assert.That(shortcut.IsEffectivelyVisible, Is.True);
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