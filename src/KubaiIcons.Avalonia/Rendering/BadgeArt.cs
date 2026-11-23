using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

internal readonly record struct BadgePlacement(KubaiIconKind Base, string Badge, double X, double Y, bool IsOverlay = false);

// Placements share the glyph block, but keep their own anchor and endpoint clips.
internal sealed class PlacedBadge(BadgePlacement placement, BadgeArt art)
{
    private readonly Matrix _anchor = Matrix.CreateTranslation(placement.X, placement.Y);
    private readonly Geometry _rest = Clip(placement, art.Radius(false));
    private readonly Geometry _held = Clip(placement, art.Radius(true));

    public void Draw(DrawingContext dc, Palette ink, Pose pose, bool semantic)
    {
        Span<double> values = stackalloc double[art.ChannelCount];
        art.Sample(pose, values);
        var clip = pose.Hover switch
        {
            0 => _rest,
            1 => _held,
            _ => Clip(placement, values[0])
        };
        using (dc.PushGeometryClip(clip))
        {
            Artwork.DrawBase(dc, placement.Base, ink, pose);
        }

        using var anchor = dc.PushTransform(_anchor);
        art.Draw(dc, ink, values, semantic);
    }

    private static CombinedGeometry Clip(BadgePlacement p, double r)
    {
        return Shape.Outside(new EllipseGeometry(new Rect(p.X - r, p.Y - r, 2 * r, 2 * r)));
    }
}

internal sealed class BadgeArt
{
    private sealed class Curve
    {
        private readonly double[] _times;
        private readonly double[] _values;

        public Curve(ArtReader r)
        {
            var n = r.Count();
            if (n == 0)
            {
                throw new InvalidDataException("Empty badge track.");
            }

            _times = new double[n];
            _values = new double[n];
            for (var i = 0; i < n; i++)
            {
                _times[i] = r.Number();
                _values[i] = r.Number();
                if (i == 0 ? _times[i] != 0 : _times[i] <= _times[i - 1])
                {
                    throw new InvalidDataException("Unordered badge track.");
                }
            }

            if (n > 1 && _times[^1] is not 1)
            {
                throw new InvalidDataException("Missing badge endpoint.");
            }
        }

        public double At(double t)
        {
            if (_values.Length == 1 || t <= 0)
            {
                return _values[0];
            }

            if (t >= 1)
            {
                return _values[^1];
            }

            var k = Array.BinarySearch(_times, t);
            if (k >= 0)
            {
                return _values[k];
            }

            k = ~k;
            return Shape.Mix(_values[k - 1], _values[k], (t - _times[k - 1]) / (_times[k] - _times[k - 1]));
        }
    }

    private readonly record struct Transform(byte Op, int X, int Y)
    {
        public Matrix At(ReadOnlySpan<double> values)
        {
            return Op switch
            {
                0 => Matrix.CreateTranslation(values[X], Y < 0 ? 0 : values[Y]),
                1 => Matrix.CreateScale(values[X], values[Y < 0 ? X : Y]),
                _ => Matrix.CreateRotation(values[X] * Math.PI / 180)
            };
        }
    }

    private sealed class Glyph
    {
        private readonly Transform[] _transforms;
        private readonly bool _disc;
        private readonly Pen? _pen;
        private readonly int _offset;
        private readonly int _left;
        private readonly int _right;
        private readonly double _dash;
        private readonly double _radius;
        private readonly Point _center;
        private readonly byte _kind;
        private readonly Geometry? _path;

        public Glyph(ArtReader r, Geometry[] paths, int channels)
        {
            _transforms = new Transform[r.Count()];
            for (var i = 0; i < _transforms.Length; i++)
            {
                var op = r.Reader.ReadByte();
                if (op > 2)
                {
                    throw new InvalidDataException("Invalid badge transform.");
                }

                _transforms[i] = new Transform(op, r.Index(channels), r.Index(channels, true));
            }

            var width = r.Number();
            var flags = r.Flags(7);
            _disc = (flags & 1) != 0;
            if (width > 0)
            {
                _pen = new Pen(
                    Brushes.White,
                    width,
                    lineCap: (flags & 2) != 0 ? PenLineCap.Round : PenLineCap.Flat,
                    lineJoin: (flags & 4) != 0 ? PenLineJoin.Round : PenLineJoin.Miter);
            }

            _dash = r.Number();
            _offset = r.Index(channels, true);
            if (_offset >= 0 && (_pen is null || _dash <= 0))
            {
                throw new InvalidDataException("Invalid dash track.");
            }

            switch (_kind = r.Reader.ReadByte())
            {
                case 0:
                    _center = r.Point();
                    _radius = r.Number();
                    break;
                case 1:
                    _left = r.Index(channels);
                    _right = r.Index(channels);
                    break;
                case 2:
                    _path = paths[r.Index(paths.Length)];
                    break;
                default:
                    throw new InvalidDataException("Invalid badge shape.");
            }
        }

        public void Draw(DrawingContext dc, IBrush disc, ReadOnlySpan<double> v)
        {
            var matrix = Matrix.Identity;
            foreach (var transform in _transforms)
            {
                matrix = transform.At(v) * matrix;
            }

            using var local = dc.PushTransform(matrix);
            var pen = _pen;
            if (_offset >= 0 && pen is not null)
            {
                // SVG specifies lengths in user space; Avalonia measures dashes in pen widths.
                var interval = _dash / pen.Thickness;
                pen = new Pen(
                    Brushes.White,
                    pen.Thickness,
                    new DashStyle([interval, interval], v[_offset] / pen.Thickness),
                    pen.LineCap,
                    pen.LineJoin);
            }

            var fill = pen is not null ? null : _disc ? disc : Brushes.White;
            switch (_kind)
            {
                case 0:
                    dc.DrawEllipse(fill, pen, _center, _radius, _radius);
                    break;
                case 1:
                    dc.DrawLine(pen!, new Point(v[_left], 0), new Point(v[_right], 0));
                    break;
                case 2:
                    dc.DrawGeometry(fill, pen, _path!);
                    break;
            }
        }
    }

    private static readonly IBrush Neutral = new SolidColorBrush(Color.FromUInt32(0xFF33363D));
    private readonly IBrush _semantic;
    private readonly (Curve Normal, Curve Wave)[] _tracks;
    private readonly Glyph[] _shapes;
    public int ChannelCount => _tracks.Length;

    public BadgeArt(ArtReader r)
    {
        _semantic = new SolidColorBrush(Color.FromUInt32(r.Reader.ReadUInt32()));
        var count = r.Count();
        if (count is < 1 or > 128)
        {
            throw new InvalidDataException("Invalid badge channel count.");
        }

        _tracks = new (Curve, Curve)[count];
        for (var i = 0; i < count; i++)
        {
            _tracks[i] = (new Curve(r), new Curve(r));
        }

        var paths = r.Paths().Select(p => p.Build()).ToArray();
        _shapes = new Glyph[r.Count()];
        for (var i = 0; i < _shapes.Length; i++)
        {
            _shapes[i] = new Glyph(r, paths, count);
        }
    }

    public double Radius(bool held) => _tracks[0].Normal.At(held ? 1 : 0);

    public void Sample(Pose pose, Span<double> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var (normal, wave) = _tracks[i];
            values[i] = normal.At(pose.Hover) + pose.WaveWeight * wave.At(pose.WaveProgress);
        }
    }

    public void Draw(DrawingContext dc, Palette ink, ReadOnlySpan<double> values, bool semantic)
    {
        var disc = ink.Badge ?? (semantic ? _semantic : Neutral);
        foreach (var glyph in _shapes)
        {
            glyph.Draw(dc, disc, values);
        }
    }
}