using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

// Immutable, shared parsed art. Per-instance transforms and track values live in
// DuotonePlayback. Native geometry is built only on the first render of this icon.
internal sealed class DuotoneArt
{
    public DuotoneData Data { get; }
    public Layer[] Layers { get; }
    public double Fit => Data.Fit;
    public int AllTrack => Data.AllTrack;

    public DuotoneArt(DuotoneData data)
    {
        Data = data;
        var isLengthRequired = new bool[data.Paths.Length];
        foreach (var layer in data.Layers)
        {
            if (layer.NeedsLength)
            {
                isLengthRequired[layer.Path] = true;
            }
        }

        var paths = new Geometry[data.Paths.Length];
        var lengths = new double[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            paths[i] = data.Paths[i].Build(isLengthRequired[i], out lengths[i]);
        }

        Layers = data.Layers.Select(layer => new Layer(layer, paths, lengths)).ToArray();
    }

    internal sealed class Layer
    {
        public DuotoneLayer Data { get; }
        public Geometry Path { get; }
        public Geometry? Clip { get; }
        public Geometry? CutClip { get; }
        public Geometry? Gap { get; }
        public double Stroke => Data.Stroke;
        public double GapStroke => Data.GapStroke;
        public double Length { get; }
        public int Track => Data.Track;
        public int[] Masks => Data.Masks;

        public Layer(DuotoneLayer data, Geometry[] paths, double[] lengths)
        {
            Data = data;
            Path = paths[data.Path];
            Length = data.NeedsLength ? lengths[data.Path] : 0;
            Clip = data.Clip >= 0 ? paths[data.Clip] : null;
            if (data.Cuts.Length > 0)
            {
                Geometry? holes = null;
                foreach (var cut in data.Cuts)
                {
                    var hole = paths[cut.Path];
                    if (cut.Stroke > 0)
                    {
                        hole = hole.GetWidenedGeometry(Pen(cut.Stroke));
                    }

                    holes = holes is null ? hole : new CombinedGeometry(GeometryCombineMode.Union, holes, hole);
                }

                CutClip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(MaskBounds), holes);
            }

            if (data.HasGap)
            {
                var halo = Path.GetWidenedGeometry(Pen(GapStroke));
                Gap = Stroke > 0 ? halo : new CombinedGeometry(GeometryCombineMode.Union, Path, halo);
            }
        }
    }

    public static Rect MaskBounds { get; } = new(-2, -2, 20, 20);

    public static Pen Pen(double width, IBrush? brush = null, IDashStyle? dash = null)
    {
        return new Pen(brush ?? Brushes.Black, width, dash, PenLineCap.Round, PenLineJoin.Round);
    }

    public static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}