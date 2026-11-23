using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

// Brushes are instance-local and replaced only when the effective colors change.
internal sealed class Palette
{
    public IBrush? Badge { get; }
    private readonly Color _cardFill;
    private readonly Color _cardEdge;
    private readonly bool _isSoft;

    private static Color ReferenceTint(Color basis, int red, int green)
    {
        static byte Apply(byte channel, double t)
        {
            return (byte)Math.Clamp(Math.Floor((t >= 0 ? channel + (255 - channel) * t : channel * (1 + t)) + 0.5), 0, 255);
        }

        var r = (red - 83) / 172d;
        return Color.FromArgb(basis.A, Apply(basis.R, r), Apply(basis.G, (green - 147) / 108d), Apply(basis.B, r));
    }

    private Dictionary<SceneArt.Paint, IBrush>? _sceneBrushes;

    public IBrush Brush(SceneArt.Paint paint)
    {
        _sceneBrushes ??= [];
        if (!_sceneBrushes.TryGetValue(paint, out var brush))
        {
            _sceneBrushes.Add(paint, brush = paint.Build(this));
        }

        return brush;
    }

    public Color SceneColor(byte mode, int rgb)
    {
        var raw = Color.FromUInt32(0xff000000u | (uint)rgb);
        return mode switch
        {
            0 => raw,
            1 => ReferenceTint(_cardFill, raw.R, raw.G),
            2 => ReferenceTint(_cardEdge, raw.R, raw.G),
            3 => _isSoft ? ReferenceTint(_cardEdge, 47, 111) : Colors.White,
            4 => Color.FromArgb((byte)rgb, 255, 255, 255),
            6 => _isSoft ? Tint(ReferenceTint(_cardFill, raw.R, raw.G), .72) : ReferenceTint(_cardFill, raw.R, raw.G),
            5 => _isSoft ? Tint(ReferenceTint(_cardEdge, raw.R, raw.G), .72) : ReferenceTint(_cardEdge, raw.R, raw.G),
            _ => throw new InvalidDataException("Unsupported scene color.")
        };
    }

    public void Shadow(DrawingContext dc, Geometry shape)
    {
        using var offset = dc.PushTransform(Matrix.CreateTranslation(0, 2));
        foreach (var pen in _shadow)
        {
            dc.DrawGeometry(null, pen, shape);
        }
    }

    public static void Highlight(DrawingContext dc, Geometry shape)
    {
        // The authored light falls on the top edge, not the whole perimeter.
        var edge = Highlights.GetValue(
            shape,
            static source =>
            {
                var shifted = source.Clone();
                shifted.Transform = new MatrixTransform(Matrix.CreateTranslation(0, .7));
                return new CombinedGeometry(GeometryCombineMode.Exclude, source, shifted);
            });
        dc.DrawGeometry(HighlightBrush, null, edge);
    }

    private readonly Pen[] _shadow;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Geometry, Geometry> Highlights = new();
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));

    public Palette(Color color, KubaiColorVariant variant, Color? badge)
    {
        var soft = variant == KubaiColorVariant.Soft;
        var fill = soft ? Tint(color, 0.1) : color;
        var edge = soft ? Tint(color, -0.16) : color;
        Color Ink(int r, int g) => Tint(fill, ((r - 83) / 172.0 + (g - 147) / 108.0) / 2);
        _cardFill = fill;
        _cardEdge = edge;
        _isSoft = soft;
        Badge = badge is { } b ? new SolidColorBrush(b) : null;
        // Approximate the SVG Gaussian on a vector-only DrawingContext: concentric
        // low-opacity strokes sample its falloff. No render target or blur per control.
        var shadow = Ink(138, 182);
        _shadow = new[] { (10d, 2), (8d, 3), (6d, 5), (4d, 8), (2d, 11), (1d, 7) }
            .Select(s => new Pen(
                new SolidColorBrush(Color.FromArgb((byte)(s.Item2 * color.A / 255), shadow.R, shadow.G, shadow.B)),
                s.Item1,
                lineJoin: PenLineJoin.Round))
            .ToArray();
    }

    public static Color Tint(Color c, double amount)
    {
        byte Channel(byte v) => (byte)Math.Clamp(Math.Round(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount)), 0, 255);
        return Color.FromArgb(c.A, Channel(c.R), Channel(c.G), Channel(c.B));
    }
}