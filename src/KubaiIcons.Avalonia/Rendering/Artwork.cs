using System.Diagnostics.Tracing;
using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

// Only rendering opens art blocks. Placements share decoded base and badge geometry on the UI thread.
internal static class Artwork
{
    private static readonly Dictionary<KubaiIconKind, DuotoneArt> Duotone = [];


    private static readonly Dictionary<string, SceneArt> Scenes = [];
    private static readonly Dictionary<string, BadgeArt> Badges = [];
    private static readonly Dictionary<KubaiIconKind, PlacedBadge> Compositions = [];

    public static DuotoneArt? GetDuotone(KubaiIconKind kind)
    {
        if (Duotone.TryGetValue(kind, out var found))
        {
            return found;
        }

        if (KubaiIconCatalog.Get(kind) is not { HasDuotone: true } info)
        {
            return null;
        }

        var art = Read("duotone-" + info.Name, ArtKind.Duotone, e => new DuotoneArt(DuotoneData.Read(e)));
        Duotone.Add(kind, art);
        return art;
    }

    public static void Draw(DrawingContext dc, KubaiIconKind kind, KubaiIconStyle style, Palette ink, Pose pose, bool isSemanticBadgeEnabled)
    {
        if (style != KubaiIconStyle.Layered)
        {
            return;
        }

        if (CompositionData.Get(kind) is { } placement)
        {
            if (placement.IsOverlay)
            {
                DrawBase(dc, placement.Base, ink, Pose.Target(false, false));
                DrawScene(dc, placement.Badge, ink, pose);
                return;
            }

            if (!Compositions.TryGetValue(kind, out var badge))
            {
                if (!Badges.TryGetValue(placement.Badge, out var art))
                {
                    Badges.Add(placement.Badge, art = Read("badge-" + placement.Badge, ArtKind.Badge, r => new BadgeArt(r)));
                }

                Compositions.Add(kind, badge = new PlacedBadge(placement, art));
            }

            badge.Draw(dc, ink, pose, isSemanticBadgeEnabled);
        }
        else
        {
            DrawBase(dc, kind, ink, pose);
        }
    }

    internal static void DrawBase(DrawingContext dc, KubaiIconKind kind, Palette ink, Pose pose)
    {
        if (KubaiIconCatalog.Get(kind) is not { HasLayered: true } info)
        {
            return;
        }

        DrawScene(dc, info.Name, ink, pose);
    }

    private static void DrawScene(DrawingContext dc, string name, Palette ink, Pose pose)
    {
        if (!Scenes.TryGetValue(name, out var scene))
        {
            Scenes.Add(name, scene = Read("scene-" + name, ArtKind.Scene, reader => new SceneArt(reader)));
        }

        scene.Draw(dc, ink, pose);
    }

    private static T Read<T>(string key, ArtKind kind, Func<ArtReader, T> parse)
    {
        using var stream = typeof(Artwork).Assembly.GetManifestResourceStream("KubaiArt." + key)
            ?? throw new InvalidDataException($"Missing icon resource: {key}.");
        try
        {
            using var reader = new ArtReader(stream, kind);
            var result = parse(reader);
            reader.Complete();
            ArtEvents.Log.BlockDecoded(key, stream.Length);
            return result;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Invalid artwork block '{key}': {e.Message}", e);
        }
    }
}

[EventSource(Name = "KubaiIcons-Avalonia")]
internal sealed class ArtEvents : EventSource
{
    public static ArtEvents Log { get; } = new();

    [Event(1, Level = EventLevel.Informational)]
    public void BlockDecoded(string name, long bytes)
    {
        WriteEvent(1, name, bytes);
    }
}

internal static class Shape
{
    public static double Mix(double a, double b, double t) => a + (b - a) * t;

    public static CombinedGeometry Outside(Geometry hole)
    {
        return new(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-40, -40, 111, 109)), hole);
    }
}