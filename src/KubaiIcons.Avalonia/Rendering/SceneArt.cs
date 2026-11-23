using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

// All indices and drawing instructions are resolved by the importer. Geometry and
// scratch registers are shared on the UI thread; paint caches belong to the effective palette.
internal sealed class SceneArt
{
    private readonly Formula[] _formulas;
    private readonly double[] _v;
    private readonly Paint[] _paints;
    private readonly ShapeData[] _shapes;
    private readonly Layer _root;
    private Pose? _last;
    private int _generation;

    public SceneArt(ArtReader r)
    {
        _formulas = new Formula[r.Count()];
        for (var i = 0; i < _formulas.Length; i++)
        {
            _formulas[i] = new Formula(r, i);
        }

        _v = new double[_formulas.Length];
        _paints = new Paint[r.Count()];
        for (var i = 0; i < _paints.Length; i++)
        {
            _paints[i] = new Paint(r);
        }

        _shapes = new ShapeData[r.Count()];
        for (var i = 0; i < _shapes.Length; i++)
        {
            _shapes[i] = new ShapeData(r, this, i);
        }

        _root = new Layer(r, this);
    }

    public void Draw(DrawingContext dc, Palette ink, Pose pose)
    {
        if (_last != pose)
        {
            for (var i = 0; i < _v.Length; i++)
            {
                _v[i] = _formulas[i].Value(_v, pose);
            }

            _last = pose;
            _generation++;
        }

        _root.Draw(dc, this, ink, 0);
    }

    private static int Ref(ArtReader r, int count)
    {
        var i = r.Reader.Read7BitEncodedInt();
        return (uint)i < (uint)count ? i : throw new InvalidDataException("Invalid scene reference.");
    }

    private static int[] Refs(ArtReader r, int count)
    {
        var a = new int[r.Count()];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = Ref(r, count);
        }

        return a;
    }

    private Matrix Transform(int[] ids)
    {
        if (ids.Length == 0)
        {
            return Matrix.Identity;
        }

        return new Matrix(_v[ids[0]], _v[ids[1]], _v[ids[2]], _v[ids[3]], _v[ids[4]], _v[ids[5]]);
    }

    private sealed class Formula
    {
        public byte Op { get; }
        private readonly int _a;
        private readonly int _b;
        private readonly int _c;
        private readonly double _constant;
        private readonly double[] _times = [];
        private readonly double[] _values = [];
        private readonly int[] _refs = [];

        public Formula(ArtReader r, int count)
        {
            Op = r.Reader.ReadByte();
            switch (Op)
            {
                case >= 6 and <= 11:
                    _a = Ref(r, count);
                    _b = Ref(r, count);
                    break;
                case 0:
                    _constant = r.Number();
                    break;
                case >= 1 and <= 5 or >= 13 and <= 15:
                    break;
                case 12 or 17:
                    _a = Ref(r, count);
                    _times = new double[r.Count()];
                    if (_times.Length == 0)
                    {
                        throw new InvalidDataException("Empty scene curve.");
                    }

                    if (Op == 12)
                    {
                        _values = new double[_times.Length];
                    }
                    else
                    {
                        _refs = new int[_times.Length];
                    }

                    for (var i = 0; i < _times.Length; i++)
                    {
                        _times[i] = r.Number();
                        if (i > 0 && _times[i] <= _times[i - 1])
                        {
                            throw new InvalidDataException("Unordered scene keys.");
                        }

                        if (Op == 12)
                        {
                            _values[i] = r.Number();
                        }
                        else
                        {
                            _refs[i] = Ref(r, count);
                        }
                    }

                    break;
                case 18 or 19:
                    _a = Ref(r, count);
                    break;
                case 16:
                    _a = Ref(r, count);
                    _b = Ref(r, count);
                    _c = Ref(r, count);
                    break;
                default:
                    throw new InvalidDataException("Unknown scene expression.");
            }
        }

        public double Value(double[] v, Pose p)
        {
            switch (Op)
            {
                case 0:
                    return _constant;
                case 1:
                    return p.Hover;
                case 2:
                    return p.Saved;
                case 3:
                    return p.SavedAtRest;
                case 4:
                    return p.WaveProgress;
                case 5:
                    return p.WaveWeight;
                case 6:
                    return v[_a] + v[_b];
                case 7:
                    return v[_a] - v[_b];
                case 8:
                    return v[_a] * v[_b];
                case 9:
                    return v[_a] / v[_b];
                case 10:
                    return Math.Max(v[_a], v[_b]);
                case 11:
                    return Math.Min(v[_a], v[_b]);
                case 13:
                    return p.StartHover;
                case 14:
                    return p.Time;
                case 15:
                    return p.IsReturning ? 1 : 0;
                case 18:
                    return Math.Sin(v[_a] * Math.PI / 180);
                case 19:
                    return Math.Cos(v[_a] * Math.PI / 180);
                case 16:
                    return v[_a] > .5 ? v[_b] : v[_c];
            }

            var t = v[_a];
            var index = Array.BinarySearch(_times, t);
            double At(int j) => Op == 12 ? _values[j] : v[_refs[j]];
            if (index >= 0)
            {
                return At(index);
            }

            index = ~index;
            if (index == 0)
            {
                return At(0);
            }

            if (index == _times.Length)
            {
                return At(index - 1);
            }

            return Shape.Mix(At(index - 1), At(index), (t - _times[index - 1]) / (_times[index] - _times[index - 1]));
        }
    }

    internal sealed class Paint
    {
        private readonly RelativePoint _start;
        private readonly RelativePoint _end;
        private readonly (double Offset, byte Mode, int Rgb)[] _stops;

        public Paint(ArtReader r)
        {
            var unit = r.Flags(1) == 1 ? RelativeUnit.Absolute : RelativeUnit.Relative;
            _start = new RelativePoint(r.Point(), unit);
            _end = new RelativePoint(r.Point(), unit);
            _stops = new (double, byte, int)[r.Count()];
            if (_stops.Length == 0)
            {
                throw new InvalidDataException("Empty scene paint.");
            }

            for (var i = 0; i < _stops.Length; i++)
            {
                _stops[i] = (r.Number(), r.Reader.ReadByte(), r.Reader.Read7BitEncodedInt());
            }
        }

        public IBrush Build(Palette ink)
        {
            if (_stops.Length == 1)
            {
                return new SolidColorBrush(ink.SceneColor(_stops[0].Mode, _stops[0].Rgb));
            }

            var gradient = new LinearGradientBrush
            {
                StartPoint = _start,
                EndPoint = _end
            };
            foreach (var (offset, mode, rgb) in _stops)
            {
                gradient.GradientStops.Add(new GradientStop(ink.SceneColor(mode, rgb), offset));
            }

            return gradient;
        }
    }

    private sealed class ShapeData
    {
        private readonly int _kind;
        private readonly bool _even;
        private readonly bool _isStatic;
        private readonly (byte Op, int[] Args)[] _ops;
        private Geometry? _geometry;
        private int _generation;

        public ShapeData(ArtReader r, SceneArt art, int index)
        {
            _kind = r.Reader.ReadByte();
            _even = r.Flags(1) != 0;
            _ops = new (byte, int[])[r.Count()];
            _isStatic = true;
            for (var i = 0; i < _ops.Length; i++)
            {
                var op = r.Reader.ReadByte();
                var args = new int[r.Count()];
                for (var j = 0; j < args.Length; j++)
                {
                    var child = j == 0 && _kind is 4 or 5;
                    args[j] = Ref(r, child ? index : art._v.Length);
                    _isStatic &= child ? art._shapes[args[j]]._isStatic : art._formulas[args[j]].Op == 0;
                }

                _ops[i] = (op, args);
            }
        }

        public Geometry Get(SceneArt art)
        {
            if (_geometry is not null && (_isStatic || _generation == art._generation))
            {
                return _geometry;
            }

            _generation = art._generation;
            _geometry = Build(art);
            return _geometry;
        }

        private Geometry Build(SceneArt art)
        {
            var v = art._v;
            if (_kind == 1 || _kind == 3)
            {
                var a = _ops[0].Args;
                var x = v[a[0]];
                var y = v[a[1]];
                var w = Math.Max(0, v[a[2]]);
                var h = Math.Max(0, v[a[3]]);
                return _kind == 1 ? new EllipseGeometry(new Rect(x - w, y - h, 2 * w, 2 * h)) : new RectangleGeometry(new Rect(x, y, w, h));
            }

            if (_kind == 4)
            {
                var best = _ops[0].Args;
                foreach (var (_, a) in _ops)
                {
                    if (v[a[1]] > v[best[1]])
                    {
                        best = a;
                    }
                }

                return art._shapes[best[0]].Get(art);
            }

            if (_kind == 5)
            {
                var group = new GeometryGroup { FillRule = FillRule.NonZero };
                foreach (var (_, a) in _ops)
                {
                    var child = art._shapes[a[0]].Get(art);
                    if (a.Length > 1)
                    {
                        child = child.Clone();
                        child.Transform = new MatrixTransform(art.Transform(a[1..]));
                    }

                    group.Children.Add(child);
                }

                return group;
            }

            var result = new StreamGeometry();
            using var path = result.Open();
            if (_kind == 2)
            {
                var points = new Point[_ops.Length];
                var radii = new double[_ops.Length];
                for (var i = 0; i < points.Length; i++)
                {
                    var a = _ops[i].Args;
                    points[i] = new Point(v[a[0]], v[a[1]]);
                    radii[i] = v[a[2]];
                }

                RoundedPath.Write(path, points, radii);
                return result;
            }

            path.SetFillRule(_even ? FillRule.EvenOdd : FillRule.NonZero);
            var open = false;
            foreach (var (op, a) in _ops)
            {
                Point P(int i) => new(v[a[i]], v[a[i + 1]]);
                switch (op)
                {
                    case 0:
                        if (open)
                        {
                            path.EndFigure(false);
                        }

                        path.BeginFigure(P(0));
                        open = true;
                        break;
                    case 1:
                        path.LineTo(P(0));
                        break;
                    case 2:
                        path.QuadraticBezierTo(P(0), P(2));
                        break;
                    case 3:
                        path.CubicBezierTo(P(0), P(2), P(4));
                        break;
                    case 4:
                        path.ArcTo(
                            P(5),
                            new Size(Math.Abs(v[a[0]]), Math.Abs(v[a[1]])),
                            v[a[2]],
                            v[a[3]] >= .5,
                            v[a[4]] >= .5 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                        break;
                    case 5:
                        path.EndFigure(true);
                        open = false;
                        break;
                    default:
                        throw new InvalidDataException("Unsupported path opcode.");
                }
            }

            if (open)
            {
                path.EndFigure(false);
            }

            return result;
        }
    }

    private sealed class Layer
    {
        private readonly int _kind;
        private readonly int _geo;
        private readonly int _fill;
        private readonly int _stroke;
        private readonly int _width;
        private readonly int _opacity;
        private readonly int _fillOpacity;
        private readonly int _strokeOpacity;
        private readonly int _offset;
        private readonly int _length;
        private readonly int _clip;
        private readonly int _clipMode;
        private readonly int _clipWidth;
        private readonly int _mask;
        private readonly byte _fx;
        private readonly PenLineCap _cap;
        private readonly PenLineJoin _join;
        private readonly int[] _matrix;
        private readonly int[] _dash;
        private readonly int[] _metrics = [];
        private readonly Layer[] _children;
        private readonly string? _text;
        private readonly Typeface _typeface;
        private readonly int _anchor;
        private readonly bool _onlyStrokes;
        private int _boundsGeneration = -1;
        private Rect _bounds;
        private Geometry? _cutSource;
        private Geometry? _cut;
        private double _cutWidth;

        public Layer(ArtReader r, SceneArt art)
        {
            var count = art._v.Length;
            _kind = r.Reader.ReadByte();
            _geo = r.Index(art._shapes.Length, true);
            _fill = r.Index(art._paints.Length, true);
            _stroke = r.Index(art._paints.Length, true);
            _matrix = Refs(r, count);
            _opacity = Ref(r, count);
            _width = Ref(r, count);
            _fillOpacity = Ref(r, count);
            _strokeOpacity = Ref(r, count);
            _offset = Ref(r, count);
            _length = Ref(r, count);
            _cap = r.Reader.ReadByte() switch
            {
                1 => PenLineCap.Round,
                2 => PenLineCap.Square,
                _ => PenLineCap.Flat
            };
            _join = r.Reader.ReadByte() switch
            {
                1 => PenLineJoin.Round,
                2 => PenLineJoin.Bevel,
                _ => PenLineJoin.Miter
            };
            _fx = r.Flags(3);
            _dash = Refs(r, count);
            _clip = r.Index(art._shapes.Length, true);
            _clipMode = r.Flags(1);
            _clipWidth = Ref(r, count);
            _mask = r.Index(art._paints.Length, true);
            if (_kind == 2)
            {
                _text = r.Reader.ReadString();
                _metrics = Refs(r, count);
                _typeface = new Typeface(FontFamily.Default, FontStyle.Normal, (FontWeight)r.Reader.Read7BitEncodedInt());
                _anchor = r.Reader.ReadByte();
            }

            _children = new Layer[r.Count()];
            for (var i = 0; i < _children.Length; i++)
            {
                _children[i] = new Layer(r, art);
            }

            _onlyStrokes = _kind == 1
                ? _stroke >= 0 && _fill < 0
                : _kind == 0 && _children.Length > 0 && Array.TrueForAll(_children, c => c._onlyStrokes);
        }

        // SVG filter regions use the un-stroked bounds. A thick open stroke can
        // extend well outside that box, even when its path is fully inside it.
        private Rect Bounds(SceneArt art)
        {
            if (_boundsGeneration == art._generation)
            {
                return _bounds;
            }

            if (_kind == 1)
            {
                _bounds = art._shapes[_geo].Get(art).Bounds;
            }
            else
            {
                _bounds = default;
                foreach (var child in _children)
                {
                    _bounds = _bounds.Union(child.Bounds(art));
                }
            }

            if (_matrix.Length != 0)
            {
                _bounds = _bounds.TransformToAABB(art.Transform(_matrix));
            }

            _boundsGeneration = art._generation;
            return _bounds;
        }

        public void Draw(DrawingContext dc, SceneArt art, Palette ink, byte fx)
        {
            var v = art._v;
            if (v[_opacity] <= 0)
            {
                return;
            }

            using var transform = _matrix.Length != 0 ? dc.PushTransform(art.Transform(_matrix)) : default;
            Rect? region = null;
            if (_fx != 0 && _onlyStrokes && _matrix.Length == 0)
            {
                var bounds = Bounds(art);
                var margin = _fx == 1 ? .4 : .1;
                region = new Rect(
                    bounds.X - margin * bounds.Width,
                    bounds.Y - margin * bounds.Height,
                    (1 + 2 * margin) * bounds.Width,
                    (_fx == 1 ? 1.9 : 1.3) * bounds.Height);
            }

            using var filtered = region is { } r ? dc.PushClip(r) : default;
            using var opacity = v[_opacity] < 1 ? dc.PushOpacity(v[_opacity]) : default;
            Geometry? clip = null;
            if (_clip >= 0)
            {
                clip = art._shapes[_clip].Get(art);
                if (_clipMode == 1)
                {
                    if (!ReferenceEquals(_cutSource, clip) || !_cutWidth.Equals(v[_clipWidth]))
                    {
                        _cutSource = clip;
                        _cutWidth = v[_clipWidth];
                        _cut = Shape.Outside(
                            _cutWidth > 0 ? clip.GetWidenedGeometry(new Pen(Brushes.Black, _cutWidth, lineCap: PenLineCap.Round)) : clip);
                    }

                    clip = _cut;
                }
            }

            using var clipped = clip is not null ? dc.PushGeometryClip(clip) : default;
            using var mask = _mask >= 0 ? dc.PushOpacityMask(ink.Brush(art._paints[_mask]), clip!.Bounds) : default;
            fx |= _fx;
            if (_kind == 0)
            {
                foreach (var child in _children)
                {
                    child.Draw(dc, art, ink, fx);
                }

                return;
            }

            var fill = _fill >= 0 ? ink.Brush(art._paints[_fill]) : null;
            if (_kind == 2)
            {
                var text = new FormattedText(_text!, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, v[_metrics[2]], fill);
                var x = v[_metrics[0]] - (_anchor == 1 ? text.Width / 2 : _anchor == 2 ? text.Width : 0);
                dc.DrawText(text, new Point(x, v[_metrics[1]] - text.Baseline));
                return;
            }

            var geo = art._shapes[_geo].Get(art);
            if (fill is not null && v[_fillOpacity] > 0)
            {
                using var alpha = v[_fillOpacity] < 1 ? dc.PushOpacity(v[_fillOpacity]) : default;
                if ((fx & 1) != 0)
                {
                    ink.Shadow(dc, geo);
                }

                dc.DrawGeometry(fill, null, geo);
                if ((fx & 2) != 0)
                {
                    Palette.Highlight(dc, geo);
                }
            }

            if (_stroke < 0 || v[_width] <= 0 || v[_strokeOpacity] <= 0)
            {
                return;
            }

            var width = v[_width];
            DashStyle? dashes = null;
            if (_dash.Length != 0)
            {
                var scale = (v[_length] > 0 ? geo.ContourLength / v[_length] : 1) / width;
                var lengths = new double[_dash.Length % 2 == 0 ? _dash.Length : _dash.Length * 2];
                var any = false;
                for (var i = 0; i < lengths.Length; i++)
                {
                    lengths[i] = Math.Max(0, v[_dash[i % _dash.Length]] * scale);
                    any |= lengths[i] > 0;
                }

                if (any)
                {
                    dashes = new DashStyle(lengths, v[_offset] * scale);
                }
            }

            using var strokeAlpha = v[_strokeOpacity] < 1 ? dc.PushOpacity(v[_strokeOpacity]) : default;
            dc.DrawGeometry(null, new Pen(ink.Brush(art._paints[_stroke]), width, dashes, _cap, _join), geo);
        }
    }
}