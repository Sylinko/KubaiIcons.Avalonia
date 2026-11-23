using KubaiIcons.Avalonia.Rendering;

namespace KubaiIcons.Importer;

internal static class DuotoneCompiler
{
    public static byte[] Compile(DuotoneSource source)
    {
        var paths = new PathTable();
        // This is also the order previously produced by canonical JSON objects.
        var tracks = source.Tracks.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        var fit = source.Key == "square" ? 13d / 12 : 1;
        var layers = source.Layers.Select((layer, index) =>
            {
                var trackIndex = Array.FindIndex(tracks, t => t.Name == layer.Name);
                var isLengthRequired = layer.Stroke > 0 && trackIndex >= 0 &&
                    (!tracks[trackIndex].Rest[7].Equals(1d) || tracks[trackIndex].Frames[7].Any(n => !n.Equals(1d)));
                return new DuotoneLayer(
                    paths.Add(layer.Path),
                    layer.Clip is { } clip ? paths.Add(clip) : -1,
                    layer.IsLight,
                    layer.Gap > 0,
                    isLengthRequired,
                    Round(layer.Stroke / fit),
                    Round((layer.Stroke + layer.Gap * 2) / fit),
                    trackIndex,
                    Enumerable.Range(index + 1, source.Layers.Length - index - 1)
                        .Where(j => source.Layers[j].Gap > 0 && source.Layers[j].Part != layer.Part)
                        .ToArray(),
                    layer.Cuts.Select(c => new DuotoneCut(paths.Add(c.Path), Round(c.Stroke / fit))).ToArray());
            })
            .ToArray();
        return ArtWriter.Block(
            ArtKind.Duotone,
            writer =>
            {
                writer.Write(Round(fit));
                writer.Write(source.Duration);
                paths.Write(writer);
                writer.Write7BitEncodedInt(tracks.Length);
                foreach (var track in tracks)
                {
                    writer.Write(track.OriginX);
                    writer.Write(track.OriginY);
                    var restMask = 0;
                    var frameMask = 0;
                    for (var c = 0; c < 8; c++)
                    {
                        if (BitConverter.DoubleToInt64Bits(track.Rest[c]) != BitConverter.DoubleToInt64Bits(DuotoneSource.Rest[c]))
                        {
                            restMask |= 1 << c;
                        }

                        if (track.Frames[c].Length > 0)
                        {
                            frameMask |= 1 << c;
                        }
                    }

                    writer.Write((byte)restMask);
                    for (var c = 0; c < 8; c++)
                    {
                        if ((restMask & (1 << c)) != 0)
                        {
                            writer.Write(track.Rest[c]);
                        }
                    }

                    writer.Write((byte)frameMask);
                    for (var c = 0; c < 8; c++)
                    {
                        if ((frameMask & (1 << c)) != 0)
                        {
                            ArtWriter.Numbers(writer, track.Frames[c]);
                        }
                    }
                }

                ArtWriter.Index(writer, Array.FindIndex(tracks, t => t.Name == "all"));
                writer.Write7BitEncodedInt(layers.Length);
                foreach (var layer in layers)
                {
                    writer.Write((byte)((layer.IsLight ? 1 : 0) | (layer.HasGap ? 2 : 0) | (layer.NeedsLength ? 4 : 0)));
                    ArtWriter.Index(writer, layer.Path);
                    ArtWriter.Index(writer, layer.Clip);
                    writer.Write(layer.Stroke);
                    writer.Write(layer.GapStroke);
                    ArtWriter.Index(writer, layer.Track);
                    writer.Write7BitEncodedInt(layer.Masks.Length);
                    foreach (var mask in layer.Masks)
                    {
                        ArtWriter.Index(writer, mask);
                    }

                    writer.Write7BitEncodedInt(layer.Cuts.Length);
                    foreach (var cut in layer.Cuts)
                    {
                        ArtWriter.Index(writer, cut.Path);
                        writer.Write(cut.Stroke);
                    }
                }
            });
    }

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}