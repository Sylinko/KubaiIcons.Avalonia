namespace KubaiIcons.Avalonia;

/// <summary>
/// Artwork family. Availability can be queried through KubaiIconCatalog.
/// </summary>
public enum KubaiIconStyle
{
    /// <summary>
    /// Outlined, layered artwork.
    /// </summary>
    Layered = 0,
    /// <summary>
    /// Solid two-tone artwork on a 16-unit grid.
    /// </summary>
    Duotone = 1,
}

/// <summary>
/// Artwork families available for a catalog entry. A control selects one <see cref="KubaiIconStyle"/> to render.
/// </summary>
[Flags]
public enum KubaiIconStyles
{
    /// <summary>
    /// No artwork is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// Layered artwork is available.
    /// </summary>
    Layered = 1,

    /// <summary>
    /// Duotone artwork is available.
    /// </summary>
    Duotone = 2,
}

/// <summary>
/// Palette used by layered icons.
/// </summary>
public enum KubaiColorVariant
{
    /// <summary>
    /// Saturated fills and white marks.
    /// </summary>
    Color = 0,
    /// <summary>
    /// Paler fills, darker edges and marks.
    /// </summary>
    Soft = 1,
}

/// <summary>
/// Determines whether a requested action runs once or repeats. Each cycle includes
/// the icon's authored entrance and return, not a reversed copy of its entrance.
/// </summary>
public enum LoopMode
{
    /// <summary>
    /// Run one complete action, then settle at the current IsEngaged target.
    /// </summary>
    Once = 0,
    /// <summary>
    /// Repeat the complete action with RepeatDelay between cycles.
    /// </summary>
    Repeat = 1,
}