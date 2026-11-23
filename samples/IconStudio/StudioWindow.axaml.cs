using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KubaiIcons.Avalonia;
using IconStudio.Localization;
using TKCode.Arc.Localization;
using TKCode.Arc.DesignTokens;
using SidebarItem = TKCode.Arc.SidebarItem;
using SidebarSection = TKCode.Arc.SidebarSection;

namespace IconStudio;

public partial class StudioWindow : TKCode.Arc.ArcWindow
{
    private readonly StudioCatalog _catalog = new();
    private StudioEntry[] _results = [];
    private StudioEntry? _selected;
    private IconInspector? _inspector;
    private Color? _accent;
    private int _columns = 8;
    private bool _isReflowQueued;
    private KubaiColorVariant _layeredPalette = KubaiColorVariant.Color;

    public StudioWindow()
    {
        InitializeComponent();
        RepositoryMark.Data = TKCode.Arc.LoginImmersiveProvider.GitHub().Mark;
        var section = new SidebarSection();
        section.Items.Add(new SidebarItem { Key = "" });
        foreach (var category in _catalog.Categories)
        {
            section.Items.Add(new SidebarItem { Key = category.Id });
        }

        Categories.Sections.Add(section);
        Categories.SelectedKey = "";
        Swatches.ItemsSource = StudioCatalog.Swatches;
        LanguageMenu.ItemClicked += (_, e) =>
        {
            if (e.Entry.CommandParameter is string tag)
            {
                ArcLanguage.Set(tag);
            }
        };
        Opened += (_, _) =>
        {
            ArcLanguage.Changed += OnLanguageChanged;
            ApplyLanguage();
        };
        Closed += (_, _) => ArcLanguage.Changed -= OnLanguageChanged;
        UpdateCategoryCounts();
        Categories.SelectionChanged += (_, _) => Filter();
        Search.TextChanged += (_, _) => Filter();
        Family.SelectionChanged += (_, _) =>
        {
            var style = CurrentStyle();
            var layered = style == KubaiIconStyle.Layered;
            var palette = layered ? _layeredPalette : KubaiColorVariant.Color;
            PaletteMode.IsEnabled = layered;
            PaletteMode.SelectedIndex = palette == KubaiColorVariant.Soft ? 1 : 0;

            foreach (var icon in _catalog.Icons)
            {
                icon.Style = style;
                icon.ColorVariant = palette;
            }

            UpdateCategoryCounts();
            if (_selected is not null)
            {
                if (!_selected.Info.SupportsStyle(style))
                {
                    CloseInspector();
                }
                else
                {
                    _inspector?.ShowIcon(_selected, CategoryTitle(_selected));
                }
            }

            Filter();
        };
        PaletteMode.SelectionChanged += (_, _) =>
        {
            if (CurrentStyle() != KubaiIconStyle.Layered)
            {
                return;
            }

            var palette = PaletteMode.SelectedIndex == 1 ? KubaiColorVariant.Soft : KubaiColorVariant.Color;
            if (_layeredPalette == palette)
            {
                return;
            }

            _layeredPalette = palette;
            foreach (var icon in _catalog.Icons)
            {
                icon.ColorVariant = palette;
            }
        };
        IconSize.ValueFormatter = value => $"{value:0} DIP";
        IconSize.ValueChanged += (_, _) => _inspector?.SetGallerySize(IconSize.Value);
        CustomColor.Bind(
            TKCode.Arc.ColorPicker.ContrastBackgroundProperty,
            CustomColor.GetResourceObservable(
                ArcDesignTokenKind.Surface,
                value =>
                    value is ISolidColorBrush surface ? surface.Color : AvaloniaProperty.UnsetValue));
        CustomColor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TKCode.Arc.ColorPicker.IsDropDownOpenProperty && CustomColor.IsDropDownOpen)
            {
                SetAccent(CustomColor.Color, custom: true);
            }
        };
        CustomColor.ColorChanged += (_, e) =>
        {
            if (CustomColor.IsDropDownOpen)
            {
                SetAccent(e.NewColor, custom: true);
            }
        };
        ClearSearch.Click += (_, _) =>
        {
            Search.Text = "";
            Categories.SelectedKey = "";
        };
        SearchHeader.SizeChanged += (_, e) =>
        {
            Library.Resources["GalleryTrackMargin"] = new Thickness(0, e.NewSize.Height, 0, 0);
            UpdateGalleryInsets();
        };
        DetailHost.SizeChanged += (_, _) => UpdateGalleryInsets();
        DetailHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateGalleryInsets();
            }
        };
        Library.GotFocus += (_, e) =>
        {
            if (e.Source is Button { DataContext: StudioEntry } tile)
            {
                var inset = Library.Padding;
                tile.BringIntoView(new Rect(0, -inset.Top, tile.Bounds.Width, tile.Bounds.Height + inset.Top + inset.Bottom));
            }
        };
        Library.SizeChanged += (_, _) =>
        {
            if (_isReflowQueued)
            {
                return;
            }

            _isReflowQueued = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _isReflowQueued = false;
                    var tileWidth = Library.Bounds.Width - Library.Padding.Left - Library.Padding.Right;
                    var columns = Math.Max(4, (int)(tileWidth / StudioRow.MinColumnWidth));
                    if (columns == _columns)
                    {
                        return;
                    }

                    var scroll = Library.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                    var anchor = (int)((scroll?.Offset.Y ?? 0) / StudioRow.Height) * _columns;
                    _columns = columns;
                    BuildRows();
                    if (anchor > 0 && Library.ItemCount > 0)
                    {
                        Library.ScrollIntoView(Math.Min(anchor / _columns, Library.ItemCount - 1));
                    }
                },
                DispatcherPriority.Loaded);
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Filter();
        SetAccent(null);
    }

    private KubaiIconStyle CurrentStyle() => Family.SelectedIndex == 0 ? KubaiIconStyle.Layered : KubaiIconStyle.Duotone;

    private string CategoryTitle(StudioEntry entry) => _catalog.Categories.First(c => c.Id == entry.Category).DisplayName;

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        foreach (var entry in _catalog.Icons)
        {
            entry.RefreshLanguage();
        }

        foreach (var swatch in StudioCatalog.Swatches)
        {
            swatch.RefreshLanguage();
        }

        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        foreach (var entry in LanguageMenu.Items.OfType<TKCode.Arc.MenuEntry>())
        {
            entry.IsChecked = Equals(entry.CommandParameter, ArcLanguage.Current.Value);
        }

        var items = Categories.Sections[0].Items;
        items[0].Label = StudioLangResources.Get(StudioLangResourceKind.AllIcons);
        for (var i = 0; i < _catalog.Categories.Count; i++)
        {
            items[i + 1].Label = _catalog.Categories[i].DisplayName;
        }

        Heading.Text = Categories.SelectedItem?.Label ?? items[0].Label;
        SetSegmentText(Family, 0, StudioLangResourceKind.Layered);
        SetSegmentText(Family, 1, StudioLangResourceKind.Duotone);
        SetSegmentText(PaletteMode, 0, StudioLangResourceKind.Color);
        SetSegmentText(PaletteMode, 1, StudioLangResourceKind.Soft);

        if (_selected is not null)
        {
            _inspector?.RefreshLanguage(CategoryTitle(_selected));
        }
    }

    private static void SetSegmentText(TKCode.Arc.SegmentedControl control, int index, StudioLangResourceKind key)
    {
        if (control.Items[index] is TKCode.Arc.SegmentedOption option)
        {
            option.Label = StudioLangResources.Get(key);
        }
    }

    // Insets belong to the scrollable content, not the viewport. Tiles can pass beneath
    // both surfaces; at either end they still come to rest in the unobstructed area.
    private void UpdateGalleryInsets()
    {
        var top = SearchHeader.Bounds.Height + 8;
        var bottom = DetailHost.IsVisible ? DetailHost.Bounds.Height + DetailHost.Margin.Bottom + 8 : 0;
        Library.Padding = new Thickness(16, top, 16, bottom);
    }

    private void UpdateCategoryCounts()
    {
        foreach (var category in Categories.Sections.SelectMany(s => s.Items))
        {
            category.Badge = _catalog.Icons.Count(i => i.Info.SupportsStyle(CurrentStyle()) &&
                    (string.IsNullOrEmpty(category.Key) || i.Category == category.Key))
                .ToString();
        }
    }

    private void Filter()
    {
        var category = Categories.SelectedItem;
        var terms = (Search.Text ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _results = _catalog.Icons.Where(i => i.Info.SupportsStyle(CurrentStyle()) &&
                (category is null || string.IsNullOrEmpty(category.Key) || i.Category == category.Key))
            .Select(i => (Icon: i, Score: i.SearchScore(terms)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Icon.Name, StringComparer.Ordinal)
            .Select(x => x.Icon)
            .ToArray();
        Heading.Text = category?.Label ?? StudioLangResources.Get(StudioLangResourceKind.AllIcons);
        NoResults.IsVisible = _results.Length == 0;
        BuildRows();
        if (Library.Scroll is { } scroll)
        {
            scroll.Offset = default;
        }
    }

    private void BuildRows()
    {
        // The outer ListBox virtualizes fixed-height rows. A row creates at most one
        // screen-width of tiles; there is no nested scroller or eager WrapPanel.
        Library.ItemsSource = _results.Chunk(_columns).Select(items => new StudioRow(items, _columns)).ToArray();
    }

    private void OnGalleryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch && e.Source is KubaiIcon { IsEffectivelyEnabled: true } icon)
        {
            icon.Play();
        }
    }

    private void OnIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StudioEntry entry })
        {
            OpenInspector(entry);
        }
    }

    private void OpenInspector(StudioEntry entry)
    {
        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        _selected = entry;
        entry.IsSelected = true;
        if (_inspector is null)
        {
            _inspector = new IconInspector();
            _inspector.SetGallerySize(IconSize.Value);
            _inspector.CloseRequested += (_, _) => CloseInspector();
            DetailHost.Content = _inspector;
        }

        _inspector.ShowIcon(entry, CategoryTitle(entry));
    }

    private void CloseInspector()
    {
        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        _selected = null;
        DetailHost.Content = null; // Detaches the player and releases the detail's visual tree.
        _inspector = null;
    }

    private void OnMixedClick(object? sender, RoutedEventArgs e)
    {
        SetAccent(null);
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex })
        {
            return;
        }

        SetAccent(Color.Parse(hex));
    }

    private void SetAccent(Color? color, bool custom = false)
    {
        _accent = color;
        foreach (var icon in _catalog.Icons)
        {
            icon.Color = color ?? icon.MixedColor;
        }

        Mixed.Classes.Set("selected", color is null);
        CustomColor.Classes.Set("selected", custom);
        CustomHint.IsVisible = !custom;
        UpdateSwatches();
    }

    private void UpdateSwatches()
    {
        foreach (var button in Swatches.GetVisualDescendants().OfType<Button>())
        {
            button.Classes.Set("selected", !CustomColor.Classes.Contains("selected") && button.Tag is string hex && Color.Parse(hex) == _accent);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key == Key.K && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) ||
            (e.Key == Key.OemQuestion && e.Source is not TextBox))
        {
            Search.Focus();
            Search.SelectAll();
            e.Handled = true;
            return;
        }

        // A popup or a filled search field owns Escape before the surrounding detail does.
        if (e.Key == Key.Escape && (CustomColor.IsDropDownOpen || LanguageMenu.IsOpen ||
                this.GetLogicalDescendants().OfType<TKCode.Arc.Popover>().Any(p => p.IsOpen) ||
                this.GetVisualDescendants().OfType<TKCode.Arc.Select>().Any(s => s.IsOpen) ||
                (ReferenceEquals(e.Source, Search) && !string.IsNullOrEmpty(Search.Text))))
        {
            return;
        }

        if (e.Key == Key.Escape && _inspector is not null)
        {
            CloseInspector();
            e.Handled = true;
            return;
        }

        var tile = (e.Source as Control)?.GetVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("tile"))
            ?? e.Source as Button;
        if (tile?.DataContext is not StudioEntry current)
        {
            return;
        }

        var index = Array.IndexOf(_results, current);
        var next = e.Key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up => index - _columns,
            Key.Down => index + _columns,
            Key.Home => 0,
            Key.End => _results.Length - 1,
            _ => index,
        };
        if (next == index || next < 0 || next >= _results.Length)
        {
            return;
        }

        Library.ScrollIntoView(next / _columns);
        Library.UpdateLayout();
        Library.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.DataContext, _results[next]))?.Focus();
        e.Handled = true;
    }
}