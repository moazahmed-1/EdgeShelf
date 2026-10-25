using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace EdgeShelf;

/// <summary>
/// CSS-style cubic-bezier easing (the same curves iOS / the web use).
/// Gives the soft "fast start, long gentle landing" motion of a Dynamic Island.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _x2;
    private readonly double _y2;

    public CubicBezierEase() : this(0.25, 0.1, 0.25, 1.0)
    {
    }

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
        EasingMode = EasingMode.EaseIn; // EaseIn = "use the function exactly as written"
    }

    protected override Freezable CreateInstanceCore()
    {
        return new CubicBezierEase(_x1, _y1, _x2, _y2);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;

        // Find the curve parameter t where x(t) == normalizedTime (bisection), then return y(t)
        double lo = 0, hi = 1, t = normalizedTime;
        for (int i = 0; i < 22; i++)
        {
            double x = Bezier(t, _x1, _x2);
            if (Math.Abs(x - normalizedTime) < 1e-5) break;
            if (x < normalizedTime) lo = t; else hi = t;
            t = (lo + hi) / 2.0;
        }
        return Bezier(t, _y1, _y2);
    }

    private static double Bezier(double t, double p1, double p2)
    {
        double u = 1.0 - t;
        return 3.0 * u * u * t * p1 + 3.0 * u * t * t * p2 + t * t * t;
    }
}
