using Avalonia;

namespace KubaiIcons.Avalonia;

/// <summary>
/// Displays a Layered icon. Named members always have artwork in this family.
/// </summary>
public sealed class KubaiLayeredIcon : KubaiIconBase
{
    /// <summary>
    /// Identifies the typed icon selection.
    /// </summary>
    public static readonly StyledProperty<KubaiLayeredIconKind> KindProperty =
        AvaloniaProperty.Register<KubaiLayeredIcon, KubaiLayeredIconKind>(nameof(Kind));

    /// <summary>
    /// The selected icon. Unknown numeric values are retained and draw nothing.
    /// </summary>
    public KubaiLayeredIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <inheritdoc />
    protected override KubaiIconKind Identity => (KubaiIconKind)Kind;

    /// <inheritdoc />
    protected override KubaiIconStyle Family => KubaiIconStyle.Layered;

    /// <inheritdoc />
    protected override bool IsSelectionProperty(AvaloniaProperty property) => property == KindProperty;
}
