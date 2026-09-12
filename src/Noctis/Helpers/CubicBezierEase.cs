using System;

namespace Noctis.Helpers;

/// <summary>
/// CSS-style cubic-bezier(x1, y1, x2, y2) as an Avalonia easing, so a curve tuned on
/// cubic-bezier.com can be pasted in verbatim.
/// </summary>
/// <remarks>
/// Not <see cref="Avalonia.Animation.Easings.SplineEasing"/>. On Avalonia 11.3.18 its
/// (x1, y1, x2, y2) constructor stores Y1 wrong (0.0 came back as 1.0), and even built via
/// property initializers a transition driven by it froze after its first frame under the
/// headless harness while a plain <see cref="Avalonia.Animation.Easings.Easing"/> subclass
/// ran to the end (probed 09-12). This class is stateless and takes the ordinary path.
/// </remarks>
public sealed class CubicBezierEase : Avalonia.Animation.Easings.Easing
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = Math.Clamp(x1, 0, 1);
        _x2 = Math.Clamp(x2, 0, 1);
        _y1 = y1;
        _y2 = y2;
    }

    public override double Ease(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        return Bezier(_y1, _y2, SolveX(progress));
    }

    private static double Bezier(double p1, double p2, double t)
    {
        var u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }

    private static double BezierDerivative(double p1, double p2, double t)
    {
        var u = 1 - t;
        return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    /// <summary>Parameter t for a given x: a few Newton steps, then bisection if they stall.</summary>
    private double SolveX(double x)
    {
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var dx = Bezier(_x1, _x2, t) - x;
            if (Math.Abs(dx) < 1e-6) return t;
            var d = BezierDerivative(_x1, _x2, t);
            if (Math.Abs(d) < 1e-6) break;
            t -= dx / d;
        }

        double lo = 0, hi = 1;
        t = x;
        for (var i = 0; i < 24; i++)
        {
            var bx = Bezier(_x1, _x2, t);
            if (Math.Abs(bx - x) < 1e-6) break;
            if (bx < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return t;
    }
}
