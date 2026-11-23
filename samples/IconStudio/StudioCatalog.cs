using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media;
using KubaiIcons.Avalonia;
using IconStudio.Localization;

namespace IconStudio;

/// <summary>
/// Studio-only browsing metadata. Loading it never opens artwork resources.
/// </summary>
public sealed class StudioCatalog
{
    public static IReadOnlyList<StudioSwatch> Swatches { get; } =
    [
        new(StudioLangResourceKind.Blue, "#669CFF"), new(StudioLangResourceKind.Violet, "#A78BFA"), new(StudioLangResourceKind.Pink, "#F472B6"),
        new(StudioLangResourceKind.Orange, "#FB923C"), new(StudioLangResourceKind.Green, "#4ADE80"),
    ];

    public IReadOnlyList<StudioEntry> Icons { get; }
    public IReadOnlyList<StudioCategory> Categories { get; }

    public StudioCatalog()
    {
        using var stream = typeof(StudioCatalog).Assembly.GetManifestResourceStream("StudioCatalog")
            ?? throw new InvalidOperationException("The Studio catalog is missing.");
        using var document = JsonDocument.Parse(stream);
        Categories = document.RootElement.GetProperty("categories")
            .EnumerateArray()
            .Select(c => new StudioCategory(
                c.GetProperty("id").GetString() ?? "",
                c.GetProperty("en").GetString() ?? "",
                c.GetProperty("zh").GetString() ?? ""))
            .ToArray();
        Icons = document.RootElement.GetProperty("icons")
            .EnumerateArray()
            .Select(e =>
            {
                var kind = Enum.Parse<KubaiIconKind>(e.GetProperty("kind").GetString() ?? "");
                var info = KubaiIconCatalog.Get(kind) ?? throw new InvalidOperationException($"Missing identity: {kind}");
                return new StudioEntry(
                    info,
                    e.GetProperty("zh").GetString() ?? throw new InvalidDataException($"Missing Chinese label for {kind}."),
                    e.GetProperty("category").GetString() ?? "",
                    e.GetProperty("keywords").EnumerateArray().Select(w => w.GetString() ?? "").ToArray());
            })
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>
/// A stable, lightweight row item. Only realized controls subscribe to its presentation properties.
/// </summary>
public sealed class StudioEntry : INotifyPropertyChanged
{
    public KubaiIconInfo Info { get; }
    public KubaiIconKind Kind => Info.Kind;
    public string Name => Info.Name;
    public string ChineseName { get; }
    public string DisplayName => StudioLanguage.IsChinese ? ChineseName : Name;
    public string AlternateName => StudioLanguage.IsChinese ? Name : ChineseName;
    public string Category { get; }
    public IReadOnlyList<string> Keywords { get; }
    public Color MixedColor { get; }
    public Color Color
    {
        get => _color;
        set => Set(ref _color, value);
    }
    public KubaiIconStyle Style
    {
        get => _style;
        set => Set(ref _style, value);
    }
    public KubaiColorVariant ColorVariant
    {
        get => _colorVariant;
        set => Set(ref _colorVariant, value);
    }
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly string _upstreamChineseName;
    private Color _color;
    private KubaiIconStyle _style = KubaiIconStyle.Layered;
    private KubaiColorVariant _colorVariant = KubaiColorVariant.Color;
    private bool _isSelected;

    public StudioEntry(KubaiIconInfo info, string chineseName, string category, string[] keywords)
    {
        Info = info;
        _upstreamChineseName = chineseName;
        ChineseName = chineseName.Contains('·')
            ? string.Join(" · ", chineseName.Split('·', StringSplitOptions.TrimEntries))
            : chineseName;
        Category = category;
        Keywords = keywords;
        // A documented, process-independent name mapping: filtering and window reflow
        // never change an icon's identity color. Do not use string.GetHashCode or Random.
        var code = 17u;
        foreach (var c in info.Name)
        {
            code = unchecked(code * 31 + c);
        }

        MixedColor = StudioCatalog.Swatches[(int)(code % (uint)StudioCatalog.Swatches.Count)].Color;
        _color = MixedColor;
    }

    internal void RefreshLanguage()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlternateName)));
    }

    public int SearchScore(string[] terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            var match = Name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 100
                : Name.Contains(term, StringComparison.OrdinalIgnoreCase) || Kind.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ? 50
                : _upstreamChineseName.Contains(term, StringComparison.OrdinalIgnoreCase) ? 40
                : Keywords.Any(w => w.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 15 : 0;
            if (match == 0)
            {
                return -1;
            }

            score += match;
        }

        return score;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// A bilingual category; the sidebar owns its current family count.
/// </summary>
public sealed class StudioCategory(string id, string name, string chineseName)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string ChineseName { get; } = chineseName;
    public string DisplayName => StudioLanguage.IsChinese ? ChineseName : Name;
}

/// <summary>
/// A bounded row inside the outer virtualizing list; no icon controls exist in this model.
/// </summary>
public sealed record StudioRow(IReadOnlyList<StudioEntry> Icons, int Columns)
{
    /// <summary>
    /// Target minimum column width including the gutter, in DIP.
    /// </summary>
    public const int MinColumnWidth = 144;

    /// <summary>
    /// Virtualized row extent including the tile margins, in DIP.
    /// </summary>
    public const double Height = 128;
}