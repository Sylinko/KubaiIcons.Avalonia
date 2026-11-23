namespace KubaiIcons.Avalonia.Rendering;

// Rendering-ready values: source labels, path text, grouping names and schema
// validation live only in the importer. All indices are local to this artwork block.
internal sealed record DuotoneData(
    double Fit,
    double Duration,
    int AllTrack,
    CompiledPath[] Paths,
    DuotoneLayer[] Layers,
    DuotoneTrack[] Tracks
)
{
    public static ReadOnlySpan<double> Rest => [0, 0, 0, 1, 1, 1, 1, 1];

    public static DuotoneData Read(ArtReader reader)
    {
        var fit = reader.Number();
        var duration = reader.Number();
        if (fit <= 0 || duration <= 0)
        {
            throw new InvalidDataException("Invalid KBI fit or duration.");
        }

        var paths = reader.Paths();
        var tracks = new DuotoneTrack[reader.Count()];
        for (var i = 0; i < tracks.Length; i++)
        {
            var x = reader.Number();
            var y = reader.Number();
            var rest = Rest.ToArray();
            var restMask = reader.Flags(255);
            for (var c = 0; c < 8; c++)
            {
                if ((restMask & (1 << c)) != 0)
                {
                    rest[c] = reader.Number();
                }
            }

            var frameMask = reader.Flags(255);
            var frames = new double[8][];
            for (var c = 0; c < 8; c++)
            {
                frames[c] = (frameMask & (1 << c)) == 0 ? [] : reader.Numbers();
            }

            tracks[i] = new DuotoneTrack(x, y, rest, frames);
        }

        var all = reader.Index(tracks.Length, true);
        var layers = new DuotoneLayer[reader.Count()];
        for (var i = 0; i < layers.Length; i++)
        {
            var flags = reader.Flags(7);
            var path = reader.Index(paths.Length);
            var clip = reader.Index(paths.Length, true);
            var stroke = reader.Number();
            var gapStroke = reader.Number();
            if (stroke < 0 || gapStroke < 0)
            {
                throw new InvalidDataException("Negative KBI stroke.");
            }

            var track = reader.Index(tracks.Length, true);
            var masks = new int[reader.Count()];
            for (var j = 0; j < masks.Length; j++)
            {
                masks[j] = reader.Index(layers.Length);
            }

            var cuts = new DuotoneCut[reader.Count()];
            for (var j = 0; j < cuts.Length; j++)
            {
                var cutPath = reader.Index(paths.Length);
                var width = reader.Number();
                if (width < 0)
                {
                    throw new InvalidDataException("Negative KBI cut stroke.");
                }

                cuts[j] = new DuotoneCut(cutPath, width);
            }

            layers[i] = new DuotoneLayer(
                path,
                clip,
                (flags & 1) != 0,
                (flags & 2) != 0,
                (flags & 4) != 0,
                stroke,
                gapStroke,
                track,
                masks,
                cuts);
        }

        return new DuotoneData(fit, duration, all, paths, layers, tracks);
    }
}

internal sealed record DuotoneLayer(
    int Path,
    int Clip,
    bool IsLight,
    bool HasGap,
    bool NeedsLength,
    double Stroke,
    double GapStroke,
    int Track,
    int[] Masks,
    DuotoneCut[] Cuts
);

internal sealed record DuotoneCut(int Path, double Stroke);

internal sealed record DuotoneTrack(double OriginX, double OriginY, double[] Rest, double[][] Frames);