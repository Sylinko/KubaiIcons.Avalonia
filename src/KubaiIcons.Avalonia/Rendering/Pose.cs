namespace KubaiIcons.Avalonia.Rendering;

// Keep the values on screen, not just a Boolean endpoint, when retargeting a motion.
internal readonly record struct Pose(double Hover, double Saved, double SavedAtRest)
{
    // Phase for the entry-only part of a badge track (e.g. an alert rattle).
    public double StartHover { get; init; }
    public double Time { get; init; }
    public bool IsReturning { get; init; }
    public double WaveProgress { get; init; }
    public double WaveWeight { get; init; }

    public static Pose Target(bool isEngaged, bool isActive)
    {
        return new Pose(isEngaged ? 1 : 0, isActive ? 1 : 0, isActive && !isEngaged ? 1 : 0)
        {
            Time = 1,
            IsReturning = !isEngaged
        };
    }

    public static Pose Between(Pose from, Pose to, double t)
    {
        if (t >= 1)
        {
            return to;
        }

        var k = Bezier(t, 0.22, 1, 0.36, 1);
        var hover = Shape.Mix(from.Hover, to.Hover, k);
        return new Pose(hover, Shape.Mix(from.Saved, to.Saved, k), Shape.Mix(from.SavedAtRest, to.SavedAtRest, k))
        {
            StartHover = from.Hover,
            Time = t,
            IsReturning = to.Hover == 0,
            WaveProgress = to.Hover == 0 ? from.Hover : hover > k + 0.001 ? k : hover,
            WaveWeight = to.Hover == 0 ? 1 - k : 1
        };
    }

    public static double Ease(double t) => Bezier(t, 0.22, 1, 0.36, 1);

    public static double Bezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        static double At(double t, double a, double b) => 3 * (1 - t) * t * ((1 - t) * a + t * b) + t * t * t;
        var lo = 0d;
        var hi = 1d;
        for (var i = 0; i < 18; i++)
        {
            var mid = (lo + hi) / 2;
            if (At(mid, x1, x2) < x)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return At((lo + hi) / 2, y1, y2);
    }
}