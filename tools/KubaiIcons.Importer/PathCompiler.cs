using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using KubaiIcons.Avalonia.Rendering;

namespace KubaiIcons.Importer;

// Uses the managed Avalonia parser with a recording context. No AppBuilder,
// platform services, native libraries, or geometry objects are initialized.
internal sealed class PathCompiler : IGeometryContext
{
    private readonly List<Command> _commands = [];

    public static byte[] Compile(string markup, FillRule fillRule)
    {
        var recorder = new PathCompiler();
        using var parser = new PathMarkupParser(recorder);
        parser.Parse((fillRule == FillRule.NonZero ? "F1 " : "F0 ") + markup);
        return recorder.Encode();
    }

    public static byte[] Rounded(Point[] points, double[] radii, double radius = 2.2)
    {
        var recorder = new PathCompiler();
        recorder.SetFillRule(FillRule.EvenOdd);
        RoundedPath.Write(recorder, points, radii, radius);
        return recorder.Encode();
    }

    public void SetFillRule(FillRule rule) => _commands.Add(new Command(PathOp.FillRule, [], [(byte)rule]));

    public void BeginFigure(Point point, bool isFilled = true) => _commands.Add(new Command(PathOp.Begin, [point.X, point.Y], [Flag(isFilled)]));

    public void LineTo(Point point, bool isStroked = true) => _commands.Add(new Command(PathOp.Line, [point.X, point.Y], [Flag(isStroked)]));

    public void QuadraticBezierTo(Point control, Point end, bool isStroked = true)
    {
        _commands.Add(new Command(PathOp.Quadratic, [control.X, control.Y, end.X, end.Y], [Flag(isStroked)]));
    }

    public void CubicBezierTo(Point first, Point second, Point end, bool isStroked = true)
    {
        _commands.Add(new Command(PathOp.Cubic, [first.X, first.Y, second.X, second.Y, end.X, end.Y], [Flag(isStroked)]));
    }

    public void ArcTo(Point end, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked = true)
    {
        _commands.Add(
            new Command(
                PathOp.Arc,
                [end.X, end.Y, size.Width, size.Height, rotationAngle],
                [Flag(isLargeArc), (byte)sweepDirection, Flag(isStroked)]));
    }

    public void EndFigure(bool isClosed) => _commands.Add(new Command(PathOp.End, [], [Flag(isClosed)]));

    public void Dispose()
    {
    }

    private byte[] Encode()
    {
        var figures = _commands.Count(c => c.Op == PathOp.Begin);
        var numbers = _commands.SelectMany(c => c.Values).ToArray();
        if (figures == 0 || numbers.Any(n => !double.IsFinite(n)))
        {
            throw new InvalidDataException("Empty or non-finite source path.");
        }

        var precision = 255;
        for (var exponent = 0; exponent <= 6; exponent++)
        {
            var scale = Math.Pow(10, exponent);
            // The bound also keeps integer conversion and ZigZag arithmetic exact.
            if (!numbers.All(n => Math.Abs(n * scale) < 9e15 &&
                    BitConverter.DoubleToInt64Bits((long)Math.Round(n * scale) / scale) == BitConverter.DoubleToInt64Bits(n)))
            {
                continue;
            }

            precision = exponent;
            break;
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)precision);
        writer.Write7BitEncodedInt(figures);
        var factor = precision == 255 ? 1 : Math.Pow(10, precision);
        foreach (var command in _commands)
        {
            writer.Write((byte)command.Op);
            foreach (var value in command.Values)
            {
                if (precision == 255)
                {
                    writer.Write(value);
                }
                else
                {
                    var integer = (long)Math.Round(value * factor);
                    writer.Write7BitEncodedInt64((integer << 1) ^ (integer >> 63));
                }
            }

            writer.Write(command.Flags);
        }

        return stream.ToArray();
    }

    private static byte Flag(bool isSet) => (byte)(isSet ? 1 : 0);

    private sealed record Command(PathOp Op, double[] Values, byte[] Flags);
}

internal sealed class PathTable
{
    private readonly List<byte[]> _paths = [];
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);

    public int Add(string source)
    {
        if (_indices.TryGetValue(source, out var existing))
        {
            return existing;
        }

        var index = _paths.Count;
        _paths.Add(PathCompiler.Compile(source, FillRule.NonZero));
        _indices.Add(source, index);
        return index;
    }

    public void Write(BinaryWriter writer) => ArtWriter.Paths(writer, _paths);
}