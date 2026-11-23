using Avalonia;
using Avalonia.Media;

namespace KubaiIcons.Avalonia.Rendering;

// Coordinates are either exact decimal fixed-point (0..6 decimal places) or Float64.
// The importer verifies bit-for-bit reconstruction before choosing fixed-point.
// No path markup parser, point array, or command object is created during replay.
internal sealed record CompiledPath(byte[] Bytes)
{
    public Geometry Build() => Build(false, out _);

    public Geometry Build(bool isLengthRequired, out double length)
    {
        using var stream = new MemoryStream(Bytes, false);
        using var reader = new BinaryReader(stream);
        var precision = reader.ReadByte();
        if (precision > 6 && precision != 255)
        {
            throw new InvalidDataException("Unknown KBI coordinate encoding.");
        }

        var scale = precision == 255 ? 1 : Math.Pow(10, precision);
        var figures = reader.Read7BitEncodedInt();
        if (figures <= 0 || figures > stream.Length - stream.Position)
        {
            throw new InvalidDataException("Invalid KBI figure count.");
        }

        double Number()
        {
            if (precision == 255)
            {
                var value = reader.ReadDouble();
                if (!double.IsFinite(value))
                {
                    throw new InvalidDataException("Non-finite KBI path coordinate.");
                }

                return value;
            }

            var encoded = (ulong)reader.Read7BitEncodedInt64();
            var signed = (long)(encoded >> 1) ^ -((long)encoded & 1);
            return signed / scale;
        }

        Point Point() => new(Number(), Number());

        bool Flag()
        {
            return reader.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("Invalid KBI path flag."),
            };
        }

        var geometry = new StreamGeometry();
        using var measurement = isLengthRequired && figures > 1 ? new ContourMeasure() : null;
        var actualFigures = 0;
        var isOpen = false;
        using (var output = geometry.Open())
        {
            while (stream.Position < stream.Length)
            {
                var op = (PathOp)reader.ReadByte();
                if (op is not (PathOp.FillRule or PathOp.Begin) && !isOpen)
                {
                    throw new InvalidDataException("KBI path command outside a figure.");
                }

                switch (op)
                {
                    case PathOp.FillRule:
                        var rule = reader.ReadByte();
                        if (rule > 1)
                        {
                            throw new InvalidDataException("Invalid KBI fill rule.");
                        }

                        output.SetFillRule((FillRule)rule);
                        break;
                    case PathOp.Begin:
                        if (isOpen)
                        {
                            throw new InvalidDataException("Nested KBI figure.");
                        }

                        var start = Point();
                        var isFilled = Flag();
                        output.BeginFigure(start, isFilled);
                        measurement?.Begin(start, isFilled);
                        isOpen = true;
                        actualFigures++;
                        break;
                    case PathOp.Line:
                        var end = Point();
                        var isLineStroked = Flag();
                        output.LineTo(end, isLineStroked);
                        measurement?.Context?.LineTo(end, isLineStroked);
                        break;
                    case PathOp.Quadratic:
                        var control = Point();
                        var quadraticEnd = Point();
                        var isQuadraticStroked = Flag();
                        output.QuadraticBezierTo(control, quadraticEnd, isQuadraticStroked);
                        measurement?.Context?.QuadraticBezierTo(control, quadraticEnd, isQuadraticStroked);
                        break;
                    case PathOp.Cubic:
                        var first = Point();
                        var second = Point();
                        var cubicEnd = Point();
                        var isCubicStroked = Flag();
                        output.CubicBezierTo(first, second, cubicEnd, isCubicStroked);
                        measurement?.Context?.CubicBezierTo(first, second, cubicEnd, isCubicStroked);
                        break;
                    case PathOp.Arc:
                        var arcEnd = Point();
                        var size = new Size(Number(), Number());
                        var rotation = Number();
                        var isLarge = Flag();
                        var direction = (SweepDirection)(Flag() ? 1 : 0);
                        var isArcStroked = Flag();
                        output.ArcTo(arcEnd, size, rotation, isLarge, direction, isArcStroked);
                        measurement?.Context?.ArcTo(arcEnd, size, rotation, isLarge, direction, isArcStroked);
                        break;
                    case PathOp.End:
                        var isClosed = Flag();
                        output.EndFigure(isClosed);
                        measurement?.End(isClosed);
                        isOpen = false;
                        break;
                    default:
                        throw new InvalidDataException("Unknown KBI path command.");
                }
            }
        }

        if (isOpen || actualFigures != figures)
        {
            throw new InvalidDataException("Incomplete KBI path.");
        }

        length = !isLengthRequired ? 0 : measurement?.Length ?? geometry.ContourLength;
        return geometry;
    }

    // Avalonia measures one contour. Only multi-contour paths whose stroke reveal
    // needs a total length allocate these temporary native geometries, during the
    // same command replay. This preserves backend length semantics without reparsing.
    private sealed class ContourMeasure : IDisposable
    {
        public StreamGeometryContext? Context { get; private set; }
        public double Length { get; private set; }
        private StreamGeometry? _geometry;

        public void Begin(Point point, bool isFilled)
        {
            _geometry = new StreamGeometry();
            Context = _geometry.Open();
            Context.BeginFigure(point, isFilled);
        }

        public void End(bool isClosed)
        {
            if (Context is null || _geometry is null)
            {
                throw new InvalidDataException("Missing KBI contour.");
            }

            Context.EndFigure(isClosed);
            Context.Dispose();
            Context = null;
            Length += _geometry.ContourLength;
            _geometry = null;
        }

        public void Dispose() => Context?.Dispose();
    }
}

internal enum PathOp : byte
{
    FillRule,
    Begin,
    Line,
    Quadratic,
    Cubic,
    Arc,
    End
}