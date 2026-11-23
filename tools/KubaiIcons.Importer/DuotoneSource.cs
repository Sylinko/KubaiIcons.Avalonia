using System.Text.Json;

namespace KubaiIcons.Importer;

// Upstream JSON is read and validated only by the offline importer. No source strings
// or schema validation enter the runtime artwork package.
internal sealed record DuotoneSource(
    string Name,
    string Key,
    SourceLayer[] Layers,
    SourceTrack[] Tracks,
    double Duration
)
{
    public static ReadOnlySpan<double> Rest => [0, 0, 0, 1, 1, 1, 1, 1];
    private static readonly string[] Channels = ["x", "y", "r", "s", "sx", "sy", "a", "w"];

    public static DuotoneSource Read(JsonElement root)
    {
        CheckKeys(root, "name", "zh", "key", "layers", "moves", "ms", "category", "tags");
        var name = root.GetProperty("name").GetString() ?? throw new InvalidDataException("Missing icon name.");
        var key = root.GetProperty("key").GetString() ?? "";
        if (key is not ("circle" or "flat" or "landscape" or "live" or "portrait" or "square" or "tall" or "wide"))
        {
            throw new InvalidDataException($"Unknown keyline '{key}' in {name}.");
        }

        var layers = root.GetProperty("layers").EnumerateArray().Select(ReadLayer).ToArray();
        if (layers.Length == 0)
        {
            throw new InvalidDataException($"No layers in {name}.");
        }

        var tracks = root.GetProperty("moves").EnumerateObject().Select(ReadTrack).ToArray();
        foreach (var track in tracks)
        {
            if (track.Name != "all" && !layers.Any(l => l.Name == track.Name))
            {
                throw new InvalidDataException($"Unknown motion target '{track.Name}' in {name}.");
            }
        }

        var frames = tracks.SelectMany(t => t.Frames).Select(a => a.Length).DefaultIfEmpty(1).Max();
        var duration = Number(root, "ms", 480 + 110 * (Math.Max(1, frames) - 1));
        if (duration <= 0)
        {
            throw new InvalidDataException($"Invalid duration in {name}.");
        }

        return new DuotoneSource(name, key, layers, tracks, duration);
    }

    private static SourceLayer ReadLayer(JsonElement e)
    {
        CheckKeys(e, "n", "d", "tone", "sw", "gap", "part", "clip", "cut");
        var name = e.GetProperty("n").ToString();
        var path = Path(e);
        var tone = e.TryGetProperty("tone", out var t) ? t.GetString() : null;
        if (tone is not (null or "a" or "b"))
        {
            throw new InvalidDataException($"Unknown tone '{tone}'.");
        }

        var stroke = Number(e, "sw", 0);
        var gap = Number(e, "gap", 0);
        if (stroke < 0 || gap < 0)
        {
            throw new InvalidDataException("Negative stroke or gap.");
        }

        var part = e.TryGetProperty("part", out var p) ? p.GetString() ?? name : name;
        var clip = e.TryGetProperty("clip", out var c) ? c.GetString() : null;
        var cuts = e.TryGetProperty("cut", out var cut)
            ? cut.EnumerateArray()
                .Select(x =>
                {
                    CheckKeys(x, "d", "sw");
                    var width = Number(x, "sw", 0);
                    if (width < 0)
                    {
                        throw new InvalidDataException("Negative cut stroke.");
                    }

                    return new SourceCut(Path(x), width);
                })
                .ToArray()
            : [];
        return new SourceLayer(name, path, tone == "b", stroke, gap, part, clip, cuts);
    }

    private static SourceTrack ReadTrack(JsonProperty entry)
    {
        var e = entry.Value;
        CheckKeys(e, "x", "y", "r", "s", "sx", "sy", "a", "w", "o", "rest");
        var rest = Rest.ToArray();
        if (e.TryGetProperty("rest", out var r))
        {
            CheckKeys(r, Channels);
            for (var i = 0; i < Channels.Length; i++)
            {
                rest[i] = Number(r, Channels[i], rest[i]);
            }
        }

        var frames = new double[Channels.Length][];
        for (var i = 0; i < frames.Length; i++)
        {
            if (!e.TryGetProperty(Channels[i], out var value))
            {
                frames[i] = [];
                continue;
            }

            frames[i] = value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(Finite).ToArray()
                : [Finite(value)];
            if (frames[i].Length == 0)
            {
                throw new InvalidDataException("An animation track cannot be empty.");
            }
        }

        var origin = e.TryGetProperty("o", out var o) ? o.EnumerateArray().Select(Finite).ToArray() : [8, 8];
        if (origin.Length != 2)
        {
            throw new InvalidDataException("An origin needs two coordinates.");
        }

        return new SourceTrack(entry.Name, origin[0], origin[1], rest, frames);
    }

    private static string Path(JsonElement e)
    {
        var path = e.GetProperty("d").GetString();
        return string.IsNullOrWhiteSpace(path) ? throw new InvalidDataException("Missing path.") : path;
    }

    private static double Number(JsonElement e, string name, double fallback)
    {
        return e.TryGetProperty(name, out var value) ? Finite(value) : fallback;
    }

    private static double Finite(JsonElement value)
    {
        var number = value.GetDouble();
        return double.IsFinite(number) ? number : throw new InvalidDataException("Non-finite coordinate.");
    }

    private static void CheckKeys(JsonElement e, params string[] allowed)
    {
        foreach (var field in e.EnumerateObject())
        {
            if (!allowed.Contains(field.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unsupported Duotone field '{field.Name}'.");
            }
        }
    }
}

internal sealed record SourceLayer(
    string Name,
    string Path,
    bool IsLight,
    double Stroke,
    double Gap,
    string Part,
    string? Clip,
    SourceCut[] Cuts
);

internal sealed record SourceCut(string Path, double Stroke);

internal sealed record SourceTrack(string Name, double OriginX, double OriginY, double[] Rest, double[][] Frames);