using Avalonia;

namespace KubaiIcons.Avalonia;

/// <summary>
/// Displays a Duotone icon. Named members always have artwork in this family.
/// </summary>
public sealed class KubaiDuotoneIcon : KubaiIconBase
{
    /// <summary>
    /// Identifies the typed icon selection.
    /// </summary>
    public static readonly StyledProperty<KubaiDuotoneIconKind> KindProperty =
        AvaloniaProperty.Register<KubaiDuotoneIcon, KubaiDuotoneIconKind>(nameof(Kind));

    /// <summary>
    /// The selected icon. Unknown numeric values are retained and draw nothing.
    /// </summary>
    public KubaiDuotoneIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <inheritdoc />
    protected override KubaiIconKind Identity => (KubaiIconKind)Kind;

    /// <inheritdoc />
    protected override KubaiIconStyle Family => KubaiIconStyle.Duotone;

    /// <inheritdoc />
    protected override bool IsSelectionProperty(AvaloniaProperty property) => property == KindProperty;
}
