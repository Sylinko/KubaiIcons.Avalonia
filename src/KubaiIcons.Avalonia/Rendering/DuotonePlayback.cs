using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

// This is a pose evaluator, not another scheduler. KubaiIcon's single playback clock
// advances it alongside the existing layered renderer and preserves its interruption pose.
internal sealed class DuotonePlayback
{
    private readonly DuotoneArt _art;
    private readonly double[][] _values;
    private readonly double[][] _from;
    private readonly LayerFrame[] _frames;
    private bool _isEngaged;
    private Color? _color;
    private IBrush _accent = Brushes.Transparent;
    private IBrush _light = Brushes.Transparent;

    public DuotonePlayback(DuotoneArt art)
    {
        _art = art;
        _values = art.Data.Tracks.Select(t => (double[])t.Rest.Clone()).ToArray();
        _from = art.Data.Tracks.Select(t => (double[])t.Rest.Clone()).ToArray();
        _frames = art.Layers.Select(_ => new LayerFrame()).ToArray();
    }

    public void Snap(bool isEngaged)
    {
        _isEngaged = isEngaged;
        Sample(1);
    }

    public void Begin(bool isEngaged)
    {
        _isEngaged = isEngaged;
        for (var i = 0; i < _values.Length; i++)
        {
            _values[i].CopyTo(_from[i], 0);
        }
    }

    public void Sample(double progress)
    {
        for (var i = 0; i < _values.Length; i++)
        {
            var track = _art.Data.Tracks[i];
            for (var c = 0; c < 8; c++)
            {
                var frames = track.Frames[c];
                var end = _isEngaged && frames.Length > 0 ? frames[^1] : track.Rest[c];
                if (progress >= 1)
                {
                    _values[i][c] = end;
                    continue;
                }

                if (!_isEngaged || frames.Length == 0)
                {
                    _values[i][c] = Shape.Mix(_from[i][c], track.Rest[c], Ease(progress, false));
                    continue;
                }

                var position = Math.Clamp(progress, 0, 1) * frames.Length;
                var segment = Math.Min(frames.Length - 1, (int)position);
                var t = position - segment;
                var first = segment == 0 ? _from[i][c] : frames[segment - 1];
                var ease = segment == frames.Length - 1 ? Ease(t, true) : InOut(t);
                _values[i][c] = Shape.Mix(first, frames[segment], ease);
            }
        }
    }

    public void Draw(DrawingContext dc, Color color)
    {
        if (_color != color)
        {
            _color = color;
            _accent = new SolidColorBrush(color);
            _light = new SolidColorBrush(Palette.Tint(color, 0.6));
        }

        var fit = _art.Fit;
        using var fitting = dc.PushTransform(
            Matrix.CreateTranslation(-8, -8) *
            Matrix.CreateScale(fit, fit) * Matrix.CreateTranslation(8, 8));
        var all = _art.AllTrack;
        using var allTransform = dc.PushTransform(MatrixFor(all));
        using var allOpacity = dc.PushOpacity(Value(all, 6));
        for (var i = 0; i < _art.Layers.Length; i++)
        {
            Prepare(i);
        }

        for (var i = 0; i < _art.Layers.Length; i++)
        {
            DrawLayer(dc, i, 0);
        }
    }

    private void Prepare(int index)
    {
        var layer = _art.Layers[index];
        var frame = _frames[index];
        var transform = MatrixFor(layer.Track);
        var amount = Value(layer.Track, 7);
        var opacity = Value(layer.Track, 6);
        var brush = layer.Data.IsLight ? _light : _accent;
        // Exact comparisons are intentional cache keys for already quantized values.
        if (!ReferenceEquals(frame.Brush, brush) || !frame.StrokeAmount.Equals(amount))
        {
            frame.Brush = brush;
            if (layer.Stroke > 0)
            {
                frame.Pen = DuotoneArt.Pen(layer.Stroke, brush, Dashes(layer, amount, layer.Stroke));
            }
        }

        if (layer.Gap is not null && (!frame.HasGap || frame.Transform != transform || !frame.StrokeAmount.Equals(amount)))
        {
            var gap = layer.Gap;
            if (layer.Stroke > 0 && amount < 1)
            {
                gap = layer.Path.GetWidenedGeometry(DuotoneArt.Pen(layer.GapStroke, dash: Dashes(layer, amount, layer.GapStroke)));
            }

            gap = gap.Clone();
            gap.Transform = new MatrixTransform(transform);
            frame.Hole = gap;
            frame.Outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(DuotoneArt.MaskBounds), gap);
            frame.HasGap = true;
            frame.Mask = null;
        }

        if (layer.Gap is not null && opacity is > 0 and < 1 && (frame.Mask is null || !frame.Opacity.Equals(opacity)))
        {
            // Most masks are opaque and use vector clipping. Only a fading gap needs
            // an alpha mask; its hole's coverage is 1 - opacity, not an opaque knockout.
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing
            {
                Geometry = frame.Outside,
                Brush = Brushes.White
            });
            var hole = new DrawingGroup { Opacity = 1 - opacity };
            hole.Children.Add(new GeometryDrawing
            {
                Geometry = frame.Hole,
                Brush = Brushes.White
            });
            drawing.Children.Add(hole);
            frame.Mask = new DrawingBrush(drawing)
            {
                SourceRect = new RelativeRect(DuotoneArt.MaskBounds, RelativeUnit.Absolute),
                DestinationRect = new RelativeRect(DuotoneArt.MaskBounds, RelativeUnit.Absolute),
                Stretch = Stretch.Fill,
            };
        }

        frame.Transform = transform;
        frame.StrokeAmount = amount;
        frame.Opacity = opacity;
    }

    private void DrawLayer(DrawingContext dc, int index, int maskIndex)
    {
        var layer = _art.Layers[index];
        if (maskIndex < layer.Masks.Length)
        {
            var mask = _frames[layer.Masks[maskIndex]];
            var maskLayer = _art.Layers[layer.Masks[maskIndex]];
            using var bounds = dc.PushClip(DuotoneArt.MaskBounds);
            if (mask.Opacity <= 0 || maskLayer.Stroke > 0 && mask.StrokeAmount < 0.01)
            {
                DrawLayer(dc, index, maskIndex + 1);
            }
            else if (mask.Opacity >= 1 && mask.Outside is { } outside)
            {
                using var gapClip = dc.PushGeometryClip(outside);
                DrawLayer(dc, index, maskIndex + 1);
            }
            else if (mask.Mask is { } brush)
            {
                using var alpha = dc.PushOpacityMask(brush, DuotoneArt.MaskBounds);
                DrawLayer(dc, index, maskIndex + 1);
            }

            return;
        }

        var frame = _frames[index];
        if (frame.Opacity <= 0 || layer.Stroke > 0 && frame.StrokeAmount < 0.01)
        {
            return;
        }

        using var transform = dc.PushTransform(frame.Transform);
        using var opacity = dc.PushOpacity(frame.Opacity);
        using var clipLayer = layer.Clip is { } clip ? dc.PushGeometryClip(clip) : default;
        using var cutLayer = layer.CutClip is { } cut ? dc.PushGeometryClip(cut) : default;
        dc.DrawGeometry(layer.Stroke > 0 ? null : frame.Brush, frame.Pen, layer.Path);
    }

    private double Value(int track, int channel)
    {
        var value = track < 0 ? DuotoneData.Rest[channel] : _values[track][channel];
        return DuotoneArt.Round(Math.Clamp(value, 0, 1));
    }

    private Matrix MatrixFor(int index)
    {
        if (index < 0)
        {
            return Matrix.Identity;
        }

        var t = _art.Data.Tracks[index];
        var v = _values[index];
        return Matrix.CreateTranslation(-t.OriginX, -t.OriginY) *
            Matrix.CreateScale(DuotoneArt.Round(v[3] * v[4]), DuotoneArt.Round(v[3] * v[5])) *
            Matrix.CreateRotation(DuotoneArt.Round(v[2]) * Math.PI / 180) *
            Matrix.CreateTranslation(DuotoneArt.Round(v[0] + t.OriginX), DuotoneArt.Round(v[1] + t.OriginY));
    }

    private static DashStyle? Dashes(DuotoneArt.Layer layer, double amount, double width)
    {
        return amount >= 1 ? null : new DashStyle([layer.Length * amount / width, layer.Length * 2 / width], 0);
    }

    private static double InOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    private static double Ease(double t, bool isEntering)
    {
        return isEntering ? Pose.Bezier(t, 0.34, 1.56, 0.64, 1) : Pose.Bezier(t, 0.33, 1, 0.68, 1);
    }

    private sealed class LayerFrame
    {
        public Matrix Transform { get; set; }
        public double StrokeAmount { get; set; } = double.NaN;
        public double Opacity { get; set; } = 1;
        public IBrush? Brush { get; set; }
        public Pen? Pen { get; set; }
        public bool HasGap { get; set; }
        public Geometry? Hole { get; set; }
        public Geometry? Outside { get; set; }
        public DrawingBrush? Mask { get; set; }
    }
}