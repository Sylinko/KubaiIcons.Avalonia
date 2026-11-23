using Avalonia;
using KubaiIcons.Avalonia.Rendering;

namespace KubaiIcons.Importer;

internal static class ArtWriter
{
    public static byte[] Block(ArtKind kind, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(ArtReader.Magic);
        writer.Write((byte)kind);
        write(writer);
        return stream.ToArray();
    }

    public static void Index(BinaryWriter writer, int index) => writer.Write7BitEncodedInt(index + 1);

    public static void Paths(BinaryWriter writer, IReadOnlyList<byte[]> paths)
    {
        writer.Write7BitEncodedInt(paths.Count);
        foreach (var path in paths)
        {
            writer.Write7BitEncodedInt(path.Length);
            writer.Write(path);
        }
    }

    public static void Numbers(BinaryWriter writer, double[] values)
    {
        writer.Write7BitEncodedInt(values.Length);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    public static void Point(BinaryWriter writer, Point point)
    {
        writer.Write(point.X);
        writer.Write(point.Y);
    }

    public static void Points(BinaryWriter writer, Point[] values)
    {
        writer.Write7BitEncodedInt(values.Length);
        foreach (var point in values)
        {
            Point(writer, point);
        }
    }

    public static void Rect(BinaryWriter writer, Rect rect)
    {
        writer.Write(rect.X);
        writer.Write(rect.Y);
        writer.Write(rect.Width);
        writer.Write(rect.Height);
    }
}