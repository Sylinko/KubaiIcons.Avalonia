using System.Text;
using Avalonia;

namespace KubaiIcons.Avalonia.Rendering;

// KBI v1 is an internal, versioned resource contract. Readers never inspect JSON or SVG.
// Counts are bounded by the remaining block before allocating; reference indices are
// checked against their local table. Resources remain independent and seekable.
internal sealed class ArtReader : IDisposable
{
    public const uint Magic = 0x0149424B; // K, B, I, version 1
    public BinaryReader Reader { get; }
    private long Remaining => Reader.BaseStream.Length - Reader.BaseStream.Position;

    public ArtReader(Stream stream, ArtKind kind)
    {
        Reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (Reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Unsupported KBI resource version.");
        }

        if (Reader.ReadByte() != (byte)kind)
        {
            throw new InvalidDataException("Unexpected KBI resource kind.");
        }
    }

    public int Count()
    {
        var count = Reader.Read7BitEncodedInt();
        if (count < 0 || count > Remaining)
        {
            throw new InvalidDataException("Invalid KBI count.");
        }

        return count;
    }

    public int Index(int count, bool isOptional = false)
    {
        var index = Reader.Read7BitEncodedInt() - 1;
        if (index < (isOptional ? -1 : 0) || index >= count)
        {
            throw new InvalidDataException("Invalid KBI reference.");
        }

        return index;
    }

    public byte Flags(byte allowed)
    {
        var flags = Reader.ReadByte();
        if ((flags & ~allowed) != 0)
        {
            throw new InvalidDataException("Unknown KBI flags.");
        }

        return flags;
    }

    public double Number()
    {
        var value = Reader.ReadDouble();
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException("Non-finite KBI value.");
        }

        return value;
    }

    public double[] Numbers()
    {
        var result = new double[Count()];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Number();
        }

        return result;
    }

    public Point Point() => new(Number(), Number());

    public Point[] Points()
    {
        var result = new Point[Count()];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Point();
        }

        return result;
    }

    public Rect Rect() => new(Number(), Number(), Number(), Number());

    public CompiledPath[] Paths()
    {
        var result = new CompiledPath[Count()];
        for (var i = 0; i < result.Length; i++)
        {
            var count = Count();
            var bytes = Reader.ReadBytes(count);
            if (bytes.Length != count)
            {
                throw new EndOfStreamException("Truncated KBI path.");
            }

            result[i] = new CompiledPath(bytes);
        }

        return result;
    }

    public void Complete()
    {
        if (Remaining != 0)
        {
            throw new InvalidDataException("Unexpected trailing KBI data.");
        }
    }

    public void Dispose() => Reader.Dispose();
}

internal enum ArtKind : byte
{
    Duotone = 0,
    Badge = 5,
    Scene = 6
}