using Avalonia;

namespace KubaiIcons.Avalonia;

/// <summary>
/// Displays a dynamically selected icon and family.
/// </summary>
public sealed class KubaiIcon : KubaiIconBase
{
    /// <summary>
    /// Identifies the Kind property.
    /// </summary>
    public static readonly StyledProperty<KubaiIconKind> KindProperty = AvaloniaProperty.Register<KubaiIcon, KubaiIconKind>(nameof(Kind));

    /// <summary>
    /// Identifies the IconStyle property.
    /// </summary>
    public static readonly StyledProperty<KubaiIconStyle> IconStyleProperty = AvaloniaProperty.Register<KubaiIcon, KubaiIconStyle>(nameof(IconStyle));

    /// <summary>
    /// Stable icon identity. None draws nothing.
    /// </summary>
    public KubaiIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>
    /// Artwork family. Unsupported combinations draw nothing.
    /// </summary>
    public KubaiIconStyle IconStyle
    {
        get => GetValue(IconStyleProperty);
        set => SetValue(IconStyleProperty, value);
    }

    /// <inheritdoc />
    protected override KubaiIconKind Identity => Kind;

    /// <inheritdoc />
    protected override KubaiIconStyle Family => IconStyle;

    /// <inheritdoc />
    protected override bool IsSelectionProperty(AvaloniaProperty property) => property == KindProperty || property == IconStyleProperty;
}
