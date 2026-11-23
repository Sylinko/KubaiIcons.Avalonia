using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using KubaiIcons.Avalonia.Rendering;

namespace KubaiIcons.Avalonia;

/// <summary>
/// Draws a color-derived, animated Kubai icon. Artwork is decoded on its first render.
/// </summary>
public abstract partial class KubaiIconBase : Control
{
    /// <summary>
    /// Identifies the Size property.
    /// </summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<KubaiIconBase, double>(nameof(Size), 24, validate: v => double.IsFinite(v) && v >= 0);

    /// <summary>
    /// Identifies the AccentColor property.
    /// </summary>
    public static readonly StyledProperty<Color?> AccentColorProperty = AvaloniaProperty.Register<KubaiIconBase, Color?>(nameof(AccentColor));

    /// <summary>
    /// Identifies the inherited foreground brush.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<KubaiIconBase>();

    /// <summary>
    /// Identifies the BadgeColor property.
    /// </summary>
    public static readonly StyledProperty<Color?> BadgeColorProperty = AvaloniaProperty.Register<KubaiIconBase, Color?>(nameof(BadgeColor));

    /// <summary>
    /// Identifies the IsSemanticBadgeEnabled property.
    /// </summary>
    public static readonly StyledProperty<bool> IsSemanticBadgeEnabledProperty =
        AvaloniaProperty.Register<KubaiIconBase, bool>(nameof(IsSemanticBadgeEnabled));

    /// <summary>
    /// Identifies the ColorVariant property.
    /// </summary>
    public static readonly StyledProperty<KubaiColorVariant> ColorVariantProperty =
        AvaloniaProperty.Register<KubaiIconBase, KubaiColorVariant>(nameof(ColorVariant));

    /// <summary>
    /// Identifies the IsEngaged property.
    /// </summary>
    public static readonly StyledProperty<bool> IsEngagedProperty = AvaloniaProperty.Register<KubaiIconBase, bool>(nameof(IsEngaged));

    /// <summary>
    /// Identifies the IsActive property.
    /// </summary>
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<KubaiIconBase, bool>(nameof(IsActive));

    /// <summary>
    /// Identifies the IsAnimationEnabled property.
    /// </summary>
    public static readonly StyledProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.Register<KubaiIconBase, bool>(nameof(IsAnimationEnabled), true, inherits: true);

    /// <summary>
    /// Desired square size in device-independent pixels; defaults to 24.
    /// </summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Overrides the inherited solid foreground color.
    /// </summary>
    public Color? AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    /// <summary>
    /// Used as the primary color when solid and AccentColor is unset.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Overrides the neutral disc color on badged artwork.
    /// </summary>
    public Color? BadgeColor
    {
        get => GetValue(BadgeColorProperty);
        set => SetValue(BadgeColorProperty, value);
    }

    /// <summary>
    /// Uses the upstream semantic disc color unless BadgeColor is set.
    /// </summary>
    public bool IsSemanticBadgeEnabled
    {
        get => GetValue(IsSemanticBadgeEnabledProperty);
        set => SetValue(IsSemanticBadgeEnabledProperty, value);
    }

    /// <summary>
    /// Color or Soft palette for layered artwork.
    /// </summary>
    public KubaiColorVariant ColorVariant
    {
        get => GetValue(ColorVariantProperty);
        set => SetValue(ColorVariantProperty, value);
    }

    /// <summary>
    /// Requests the held action pose. False returns to rest; playback temporarily owns this motion.
    /// </summary>
    public bool IsEngaged
    {
        get => GetValue(IsEngagedProperty);
        set => SetValue(IsEngagedProperty, value);
    }

    /// <summary>
    /// Persistent state for supporting artwork, such as the saved ribbon on layered Bookmark and its badge variants.
    /// </summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// When false, all pose changes are immediate, including saved-state changes.
    /// </summary>
    public bool IsAnimationEnabled
    {
        get => GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    /// <summary>
    /// The artwork identity to render. Defaults to None, which renders nothing.
    /// </summary>
    protected abstract KubaiIconKind Identity { get; }

    /// <summary>
    /// The artwork family to render. Defaults to Duotone, which renders the duotone variant if available.
    /// </summary>
    protected abstract KubaiIconStyle Family { get; }

    private static readonly Color ReferenceBlue = Color.FromRgb(83, 147, 255);
    private Palette? _palette;
    private DuotonePlayback? _duotone;
    private (Color, KubaiColorVariant, Color?) _paletteKey;

    static KubaiIconBase()
    {
        AffectsMeasure<KubaiIconBase>(SizeProperty);
        AffectsRender<KubaiIconBase>(
            SizeProperty,
            AccentColorProperty,
            ForegroundProperty,
            BadgeColorProperty,
            ColorVariantProperty,
            IsSemanticBadgeEnabledProperty);
    }

    /// <summary>
    /// Returns true if the given property is a selection property that should be reset when the icon is deselected.
    /// </summary>
    /// <param name="property"></param>
    /// <returns></returns>
    protected abstract bool IsSelectionProperty(AvaloniaProperty property);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var box = Bounds.Size;
        var extent = Math.Min(Size, Math.Min(box.Width, box.Height));
        if (extent <= 0 || Identity == KubaiIconKind.None)
        {
            return;
        }

        context.DrawRectangle(Brushes.Transparent, null, new Rect(box));
        var color = AccentColor ?? (Foreground as ISolidColorBrush)?.Color ?? ReferenceBlue;
        var isDuotone = Family == KubaiIconStyle.Duotone;
        var scale = extent / (isDuotone ? 16 : 31);
        using var transform = context.PushTransform(
            Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((box.Width - extent) / 2, (box.Height - extent) / 2 + (isDuotone ? 0 : 3 * scale)));
        if (isDuotone)
        {
            if (_duotone is null)
            {
                if (Artwork.GetDuotone(Identity) is not { } art)
                {
                    return;
                }

                _duotone = new DuotonePlayback(art);
                _duotone.Snap(_from.Hover >= 1);
                if (_phase is Phase.Target or Phase.Outbound or Phase.Returning)
                {
                    _duotone.Begin(_target.Hover >= 1);
                    _duotone.Sample(Math.Min(1, _elapsed / _duration));
                }
                else
                {
                    _duotone.Snap(_pose.Hover >= 1);
                }
            }

            _duotone.Draw(context, color);
            return;
        }

        var key = (color, ColorVariant, BadgeColor);
        if (_palette is null || _paletteKey != key)
        {
            _paletteKey = key;
            _palette = new Palette(color, ColorVariant, BadgeColor);
        }

        var pose = _pose with
        {
            Saved = _saved
        };
        Artwork.Draw(context, Identity, Family, _palette, pose, IsSemanticBadgeEnabled);
    }
}
