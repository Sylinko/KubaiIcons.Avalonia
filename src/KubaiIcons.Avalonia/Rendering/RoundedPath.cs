using Avalonia;
using Avalonia.Platform;

namespace KubaiIcons.Avalonia.Rendering;

// Port of upstream rp(): a backend-free emitter shared by import-time static
// paths and runtime morphing. Keep the arithmetic identical at both call sites.
internal static class RoundedPath
{
    public static void Write(IGeometryContext path, ReadOnlySpan<Point> points, ReadOnlySpan<double> radii, double radius = 2.2)
    {
        for (var i = 0; i < points.Length; i++)
        {
            var v = points[i];
            var prev = points[(i + points.Length - 1) % points.Length] - v;
            var next = points[(i + 1) % points.Length] - v;
            var r = radii.IsEmpty ? radius : radii[i];
            var a = v + prev * Math.Min(r / Math.Max(double.Hypot(prev.X, prev.Y), 1e-6), 0.5);
            var b = v + next * Math.Min(r / Math.Max(double.Hypot(next.X, next.Y), 1e-6), 0.5);
            if (i == 0)
            {
                path.BeginFigure(a);
            }
            else
            {
                path.LineTo(a);
            }

            path.QuadraticBezierTo(v, b);
        }

        path.EndFigure(true);
    }
}