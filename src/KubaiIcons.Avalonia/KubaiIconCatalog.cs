using System.Collections;
using KubaiIcons.Avalonia.Rendering;

namespace KubaiIcons.Avalonia;

/// <summary>
/// Lightweight icon metadata. Reading the catalog never decodes paths or opens artwork resources.
/// </summary>
public static class KubaiIconCatalog
{
    /// <summary>
    /// Available identities in stable declaration order, excluding None.
    /// </summary>
    public static IReadOnlyList<KubaiIconInfo> All => CatalogView.Instance;

    /// <summary>
    /// Returns metadata for an available identity, or null for None and unknown values.
    /// </summary>
    public static KubaiIconInfo? Get(KubaiIconKind kind)
    {
        var id = (int)kind;
        if (id <= 0 || id > Storage.ById.Length)
        {
            return null;
        }

        var info = Storage.ById[id - 1];
        return info.Kind == kind ? info : null;
    }

    // Reading All.Count needs only the generated count, not the metadata rows.
    private static class Storage
    {
        internal static KubaiIconInfo[] ById { get; } = CatalogData.Create();
    }

    private sealed class CatalogView : IReadOnlyList<KubaiIconInfo>
    {
        public static CatalogView Instance { get; } = new();

        public int Count => CatalogData.Count;

        public KubaiIconInfo this[int index]
        {
            get
            {
                if ((uint)index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return Storage.ById[CatalogData.SlotAt(index)];
            }
        }

        public IEnumerator<KubaiIconInfo> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return Storage.ById[CatalogData.SlotAt(index)];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// Describes an identity's supported artwork families and state capabilities.
/// </summary>
public readonly struct KubaiIconInfo
{
    /// <summary>
    /// The permanent public identity.
    /// </summary>
    public KubaiIconKind Kind { get; }

    /// <summary>
    /// The upstream kebab-case name, suitable for search and display.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The set of artwork families available for this identity.
    /// </summary>
    public KubaiIconStyles SupportedStyles { get; }

    /// <summary>
    /// Whether layered artwork is available.
    /// </summary>
    public bool HasLayered => (SupportedStyles & KubaiIconStyles.Layered) != 0;

    /// <summary>
    /// Whether two-tone artwork is available.
    /// </summary>
    public bool HasDuotone => (SupportedStyles & KubaiIconStyles.Duotone) != 0;

    internal KubaiIconInfo(KubaiIconKind kind, string name, KubaiIconStyles supportedStyles)
    {
        Kind = kind;
        Name = name;
        SupportedStyles = supportedStyles;
    }

    /// <summary>
    /// Whether this identity has artwork in the requested family.
    /// </summary>
    public bool SupportsStyle(KubaiIconStyle style)
    {
        return style switch
        {
            KubaiIconStyle.Layered => HasLayered,
            KubaiIconStyle.Duotone => HasDuotone,
            _ => false
        };
    }

    /// <summary>
    /// Whether IsActive affects this artwork.
    /// </summary>
    public bool SupportsActiveState(KubaiIconStyle style) => style == KubaiIconStyle.Layered && CompositionData.SupportsActive(Kind);

    /// <summary>
    /// Whether ColorVariant affects this artwork.
    /// </summary>
    public bool SupportsSoftPalette(KubaiIconStyle style) => style == KubaiIconStyle.Layered && HasLayered;
}

/// <summary>
/// Converts a supported icon identity without loading artwork.
/// </summary>
public static class KubaiIconConversions
{
    /// <summary>
    /// Returns true for None or an identity supported by both families.
    /// </summary>
    public static bool TryToLayered(this KubaiDuotoneIconKind kind, out KubaiLayeredIconKind result)
    {
        var id = (KubaiIconKind)kind;
        if (id == KubaiIconKind.None || KubaiIconCatalog.Get(id) is { HasDuotone: true, HasLayered: true })
        {
            result = (KubaiLayeredIconKind)id;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Returns true for None or an identity supported by both families.
    /// </summary>
    public static bool TryToDuotone(this KubaiLayeredIconKind kind, out KubaiDuotoneIconKind result)
    {
        var id = (KubaiIconKind)kind;
        if (id == KubaiIconKind.None || KubaiIconCatalog.Get(id) is { HasDuotone: true, HasLayered: true })
        {
            result = (KubaiDuotoneIconKind)id;
            return true;
        }

        result = default;
        return false;
    }
}