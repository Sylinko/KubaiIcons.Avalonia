using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using IconStudio;
using NUnit.Framework;
using TKCode.Arc.Localization;
using TKCode.Arc.Motion;
using Arc = TKCode.Arc;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test]
    public async Task Studio_WhenSwitchingLanguage_KeepsSelectionSearchAndCode()
    {
        await _session.Dispatch(
            async () =>
            {
                var window = new StudioWindow
                {
                    Width = 1100,
                    Height = 740
                };
                var original = ArcLanguage.Current;
                ArcMotion.SetLevel(window, ArcMotionLevel.Off);
                T Find<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new AssertionException(name);

                void Click(Control control)
                {
                    var top = TopLevel.GetTopLevel(control) ?? throw new AssertionException("Detached control.");
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)
                        ?? throw new AssertionException("Control not arranged.");
                    top.MouseMove(point);
                    top.MouseDown(point, MouseButton.Left);
                    top.MouseUp(point, MouseButton.Left);
                    Pump();
                }

                void ChangeLanguage(string name)
                {
                    Click(Find<Button>("LanguageButton"));
                    var menu = Find<Arc.DropdownMenu>("LanguageMenu");
                    Assert.That(menu.IsOpen, Is.True);
                    var label = window.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .First(t => t.IsEffectivelyVisible && t.Text == name);
                    Capture(window, "studio-language-menu");
                    Click(label);
                    Assert.That(menu.IsOpen, Is.False);
                }

                try
                {
                    ArcLanguage.Set(LanguageTags.EnUS);
                    window.Show();
                    Pump();
                    var search = Find<TextBox>("Search");
                    Find<Arc.Sidebar>("Categories").SelectedKey = "files";
                    search.Text = "目录";
                    Pump();
                    var grid = Find<ListBox>("Library");
                    var rows = grid.ItemsSource;
                    var tile = grid.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is StudioEntry { Name: "folder" });
                    Click(tile);
                    var detail = Find<ContentControl>("DetailHost").Content as IconInspector ?? throw new AssertionException("Missing detail.");
                    var code = detail.FindControl<Arc.CodeBlock>("Snippet") ?? throw new AssertionException("Missing code.");
                    var axaml = code.Code;
                    var segment = detail.FindControl<Arc.SegmentedControl>("CodeLanguage") ?? throw new AssertionException("Missing code language.");
                    segment.SelectedIndex = 1;
                    Pump();
                    var cs = code.Code;
                    using var reads = new DecodeListener();
                    ChangeLanguage("简体中文");
                    Assert.That(ArcLanguage.Current, Is.EqualTo(LanguageTags.ZhCN));
                    Assert.That(grid.ItemsSource, Is.SameAs(rows));
                    Assert.That(Find<ContentControl>("DetailHost").Content, Is.SameAs(detail));
                    Assert.That(search.Text, Is.EqualTo("目录"));
                    Assert.That(Find<TextBlock>("Heading").Text, Is.EqualTo("文件与文档"));
                    Assert.That(Find<TextBlock>("SizeLabel").Text, Is.EqualTo("大小"));
                    Assert.That(Find<Arc.SearchField>("Search").Placeholder, Does.Contain("形状"));
                    Assert.That(Find<Arc.SegmentedControl>("Family").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "分层"), Is.True);
                    Assert.That(tile.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "文件夹"), Is.True);
                    Assert.That(Arc.Tooltip.GetTip(tile), Is.EqualTo("folder"));
                    Assert.That(detail.FindControl<TextBlock>("IconName")?.Text, Is.EqualTo("文件夹"));
                    Assert.That(detail.FindControl<Arc.Switch>("Hold")?.Content, Is.EqualTo("保持动作姿态"));
                    Assert.That(code.Code, Is.EqualTo(cs));
                    Assert.That(reads.Names, Is.Empty, "Language switches change labels, not decoded artwork.");
                    window.MouseMove(new Point(2, 2));
                    Pump();
                    Capture(window, "studio-zh");
                    segment.SelectedIndex = 0;
                    Pump();
                    Assert.That(code.Code, Is.EqualTo(axaml));
                    Click(detail.FindControl<Button>("MotionOptions") ?? throw new AssertionException("Missing motion settings."));
                    var loops = detail.FindControl<Arc.Select>("Loops") ?? throw new AssertionException("Missing loop selector.");
                    Assert.That(loops.Label, Is.EqualTo("循环模式"));
                    Assert.That(loops.SelectedText, Is.EqualTo("单次"));
                    Capture(window, "studio-zh-animation");
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Pump();
                    foreach (var query in new[] { "档案夹·同步", "档案夹 · 同步" })
                    {
                        search.Text = query;
                        Pump();
                        Assert.That(
                            grid.Items.Cast<StudioRow>().SelectMany(r => r.Icons).Select(i => i.Name),
                            Is.EquivalentTo(new[] { "binder-sync" }));
                    }

                    var binder = grid.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is StudioEntry { Name: "binder-sync" });
                    Assert.That(binder.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "档案夹 · 同步"), Is.True);
                    Click(binder);
                    var title = detail.FindControl<TextBlock>("IconName") ?? throw new AssertionException("Missing title.");
                    var member = detail.FindControl<TextBlock>("EnumName") ?? throw new AssertionException("Missing enum name.");
                    var copy = detail.FindControl<Arc.CopyButton>("EnumCopy") ?? throw new AssertionException("Missing copy target.");
                    Assert.That(title.Text, Is.EqualTo("档案夹 · 同步"));
                    var badge = detail.FindControl<Arc.Badge>("CategoryBadge") ?? throw new AssertionException("Missing category badge.");
                    Assert.That(badge.Content, Is.EqualTo("文件与文档"));
                    var enumBottom = copy.TranslatePoint(new Point(0, copy.Bounds.Height), detail) ?? throw new AssertionException("Detached enum.");
                    var badgeTop = badge.TranslatePoint(default, detail) ?? throw new AssertionException("Detached badge.");
                    Assert.That(badgeTop.X, Is.EqualTo(enumBottom.X).Within(1));
                    Assert.That(badgeTop.Y, Is.GreaterThan(enumBottom.Y));
                    Assert.That(member.Text, Is.EqualTo("BinderSync"));
                    Assert.That(member.FontSize, Is.EqualTo(title.FontSize));
                    Assert.That(member.Foreground, Is.Not.EqualTo(title.Foreground));
                    Assert.That(Arc.Tooltip.GetTip(copy), Is.Null);
                    Click(member);
                    Assert.That(
                        await (window.Clipboard ?? throw new AssertionException("No clipboard.")).TryGetTextAsync(),
                        Is.EqualTo("BinderSync"));
                    Assert.That(copy.State, Is.EqualTo(Arc.CopyState.Copied));
                    Assert.That(Arc.Tooltip.GetTip(copy), Is.Null);
                    Assert.That(member.Text, Is.EqualTo("BinderSync"));
                    window.Width = 1440;
                    window.Height = 960;
                    window.FindControl<Arc.Slider>("IconSize")!.Value = 56;
                    search.Text = "";
                    Pump();
                    var binderRow = grid.Items.Cast<StudioRow>()
                        .Select((row, i) => (row, i))
                        .First(pair => pair.row.Icons.Any(entry => entry.Name == "binder-sync"))
                        .i;
                    grid.ScrollIntoView(binderRow);
                    Pump();
                    var selectedTile = grid.GetVisualDescendants()
                        .OfType<Button>()
                        .Single(b => b.DataContext is StudioEntry { Name: "binder-sync" });
                    selectedTile.Focus();
                    Pump();
                    window.MouseMove(new Point(2, 2));
                    Pump();
                    Capture(window, "studio-binder-sync");
                    ChangeLanguage("English");
                    Assert.That(Find<TextBlock>("Heading").Text, Is.EqualTo("Files & Documents"));
                    Assert.That(title.Text, Is.EqualTo("binder-sync"));
                    Assert.That(badge.Content, Is.EqualTo("Files & Documents"));
                    Assert.That(copy.Label, Is.EqualTo("Copy enum name BinderSync"));
                    Assert.That(
                        Arc.Tooltip.GetTip(
                            grid.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is StudioEntry { Name: "binder-sync" })),
                        Is.EqualTo("档案夹 · 同步"));
                    Assert.That(code.Code, Does.Contain("Kind=\"BinderSync\""));
                }
                finally
                {
                    window.Close();
                    ArcLanguage.Set(original);
                }
            },
            CancellationToken.None);
    }
}