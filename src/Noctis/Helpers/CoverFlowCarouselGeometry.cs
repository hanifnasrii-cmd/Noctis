using System;

namespace Noctis.Helpers;

/// <summary>One card's place in the Cover Flow carousel at a (fractional) position.</summary>
/// <param name="X">Horizontal offset from the centre slot, logical px (negative = left).</param>
/// <param name="Scale">Uniform scale (1 at the centre).</param>
/// <param name="AngleY">3D tilt about the vertical axis, degrees. Left cards are NEGATIVE
/// so their right edge (the one nearest the centre) is the near edge; mirrored on the right.</param>
/// <param name="Dim">0..1 black wash (depth cue).</param>
/// <param name="Opacity">Card opacity: the ±3 entry/exit slot is fully transparent so a
/// card sliding in from off-row fades in rather than popping.</param>
public readonly record struct CarouselPose(double X, double Scale, double AngleY, double Dim, double Opacity);

/// <summary>
/// Pure slot geometry for the five-card Cover Flow carousel (centre + 2 each side), so the
/// view can interpolate a card smoothly between slots during a skip and the tests can pin
/// the numbers without a visual tree. Positions are signed: 0 = centre, −1/−2 = history,
/// +1/+2 = up next; |position| is clamped to <see cref="ExitSlot"/>.
/// </summary>
public static class CoverFlowCarouselGeometry
{
    /// <summary>Slots on each side of the centre that hold a real card.</summary>
    public const int SideSlots = 2;

    /// <summary>One slot past the last visible one: where a card starts (fading in) when it
    /// enters the row and ends (faded out) when it leaves.</summary>
    public const int ExitSlot = SideSlots + 1;

    /// <summary>Perspective depth for the 3D tilt (Rotate3DTransform.Depth).</summary>
    public const double Depth = 1400;

    /// <summary>Length of the slide when the centre track changes.</summary>
    public static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(360);

    // Keyframes by |position| 0,1,2,3. Tuned against the reference: the ±1 card shows about
    // half the centre card's width beside it, ±2 about a third, each tucked BEHIND its inner
    // neighbour; ±3 is off-row and transparent.
    private static readonly double[] Xs = { 0, 225, 385, 520 };
    private static readonly double[] Scales = { 1.00, 0.85, 0.70, 0.58 };
    private static readonly double[] Angles = { 0, 18, 28, 34 };
    private static readonly double[] Dims = { 0, 0.08, 0.20, 0.32 };
    private static readonly double[] Opacities = { 1, 1, 0.90, 0 };

    /// <summary>Pose for a signed, possibly fractional, slot position.</summary>
    public static CarouselPose At(double position)
    {
        var sign = position < 0 ? -1 : 1;
        var d = Math.Min(Math.Abs(position), ExitSlot);
        var i = (int)Math.Floor(d);
        if (i >= ExitSlot) i = ExitSlot - 1;
        var t = d - i;

        double Lerp(double[] k) => k[i] + (k[i + 1] - k[i]) * t;

        return new CarouselPose(
            X: sign * Lerp(Xs),
            Scale: Lerp(Scales),
            AngleY: sign * Lerp(Angles),
            Dim: Lerp(Dims),
            Opacity: Lerp(Opacities));
    }

    /// <summary>Draw order: the centre on top, falling off with distance (ties impossible
    /// between two cards a whole slot apart during a slide).</summary>
    public static int ZIndexAt(double position) => 100 - (int)Math.Round(Math.Abs(position) * 10);

    /// <summary>Ease-out cubic for the slide.</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var u = 1 - t;
        return 1 - u * u * u;
    }
}
