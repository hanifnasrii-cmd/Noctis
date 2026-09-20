using System;

namespace Noctis.Helpers;

/// <summary>One card's place in the Cover Flow carousel at a (fractional) position.</summary>
/// <param name="X">Horizontal offset from the centre slot, logical px (negative = left).</param>
/// <param name="Scale">Uniform scale (1 at the centre).</param>
/// <param name="AngleY">3D tilt about the vertical axis, degrees. Left cards are NEGATIVE
/// so their right edge (the one nearest the centre) is the near edge; mirrored on the right.</param>
/// <param name="Dim">0..1 black wash (depth cue).</param>
/// <param name="Opacity">Card opacity: the entry/exit slot past the last real one is fully
/// transparent so a card sliding in from off-row fades in rather than popping.</param>
/// <param name="Y">Vertical offset, logical px (negative = up): the float arc lifts the
/// outer cards a few px above the centre one.</param>
public readonly record struct CarouselPose(double X, double Scale, double AngleY, double Dim, double Opacity, double Y = 0);

/// <summary>
/// Pure slot geometry for the Cover Flow carousel (centre + <see cref="SideSlots"/> each
/// side), so the view can interpolate a card smoothly between slots during a skip and the
/// tests can pin the numbers without a visual tree. Positions are signed: 0 = centre,
/// negative = history, positive = up next; |position| is clamped to <see cref="ExitSlot"/>.
///
/// The look (09-17 r6, the reference mockup): FIVE cards on one midline, the centre card
/// floating IN FRONT of the ±1 cards it overlaps (their captions cut mid-word by it, on
/// purpose), ±1 tilted 40° with real perspective, ±2 tilted 49° and overlapped by ±1;
/// each step outward closer, smaller, dimmer and a few px higher (float arc). Every
/// number is solved from the mockup's ratios in the constants below; no per-slot table.
/// </summary>
public static class CoverFlowCarouselGeometry
{
    // ── Tuning ────────────────────────────────────────────────────────────────

    /// <summary>Slots on each side of the centre that hold a real card.</summary>
    public const int SideSlots = 2;

    /// <summary>One slot past the last visible one: where a card starts (fading in) when it
    /// enters the row and ends (faded out) when it leaves.</summary>
    public const int ExitSlot = SideSlots + 1;

    /// <summary>Perspective depth for the 3D tilt (Rotate3DTransform.Depth). Lower = stronger
    /// foreshortening: at 1400 a 30° tilt of a 320px card painted as a ~13% narrower flat
    /// rectangle (near edge ×1.06, far ×0.95) and read as flat; 610 is solved from the
    /// reference mockup's 1.3 near/far edge ratio on the ±1 card (dz = 160·0.77·sin 40°).</summary>
    public const double Depth = 610;

    /// <summary>Length of the slide for a one-slot skip.</summary>
    public static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(360);

    /// <summary>Added per extra slot of travel, so a jump to a far card (the row moves
    /// ~1000px) reads as a slide instead of a blink: 1 → 360ms, 4 → 630ms, 8 → 990ms.</summary>
    public static readonly TimeSpan SlidePerExtraSlot = TimeSpan.FromMilliseconds(90);

    /// <summary>Slide length for a skip of <paramref name="step"/> slots (sign ignored).</summary>
    public static TimeSpan SlideDurationFor(int step) =>
        SlideDuration + SlidePerExtraSlot * Math.Max(0, Math.Abs(step) - 1);

    /// <summary>X of the ±1 card, solved so the centre card covers 22.5% of the ±1 card's
    /// projected face (reference mockup: 20-25%) — it floats in front of a stack, not
    /// beside tiles.</summary>
    public const double CenterGap = 225;

    /// <summary>Each further step outward is this fraction of the previous step, solved so
    /// the ±1 card covers 30% of the ±2 card's projected face (reference: 25-35%).</summary>
    public const double StepShrink = 0.54;

    /// <summary>Tilt of the ±1 card, degrees — hard from the first slot.</summary>
    public const double FirstAngle = 40;

    /// <summary>Tilt ramps linearly from <see cref="FirstAngle"/> to this cap…</summary>
    public const double AngleCap = 49;

    /// <summary>…reaching it at this slot, then holds constant further out.</summary>
    public const int AngleCapSlot = 2;

    /// <summary>Scale of the ±1 card — clearly smaller than the centre at a glance.</summary>
    public const double FirstScale = 0.77;

    /// <summary>Each further card is this fraction of the previous one's scale…</summary>
    public const double ScaleStep = 0.87;

    /// <summary>…never below this.</summary>
    public const double ScaleFloor = 0.45;

    /// <summary>Opacity of the ±1 card.</summary>
    public const double FirstOpacity = 0.90;

    /// <summary>Opacity drops by this much per further slot…</summary>
    public const double OpacityStep = 0.08;

    /// <summary>…never below this (the exit slot is 0 regardless).</summary>
    public const double OpacityFloor = 0.35;

    /// <summary>Black wash per slot outward (depth cue on top of the opacity fade)…</summary>
    public const double DimStep = 0.05;

    /// <summary>…capped here.</summary>
    public const double DimMax = 0.45;

    /// <summary>Float arc: every slot outward sits this many px HIGHER than the centre card
    /// (±1 = −4, ±2 = −8 … ), so the row reads as cards hanging in space, not on a shelf.</summary>
    public const double ArcRisePerSlot = 4;

    // ── Derived keyframes by |position| 0..ExitSlot ───────────────────────────

    private static readonly double[] Xs = Build(XAt);
    private static readonly double[] Scales = Build(ScaleAt);
    private static readonly double[] Angles = Build(AngleAt);
    private static readonly double[] Dims = Build(DimAt);
    private static readonly double[] Opacities = Build(OpacityAt);
    private static readonly double[] Ys = Build(YAt);

    private static double[] Build(Func<int, double> f)
    {
        var k = new double[ExitSlot + 1];
        for (var i = 0; i <= ExitSlot; i++) k[i] = f(i);
        return k;
    }

    /// <summary>X(k) = CenterGap · (1 + s + s² + … + s^(k−1)), s = <see cref="StepShrink"/>:
    /// a geometric series, so each step is <see cref="StepShrink"/> of the one before.</summary>
    public static double XAt(int slot)
    {
        double x = 0, step = CenterGap;
        for (var i = 0; i < slot; i++) { x += step; step *= StepShrink; }
        return x;
    }

    /// <summary>Scale(k) = max(<see cref="ScaleFloor"/>, FirstScale · ScaleStep^(k−1)).</summary>
    public static double ScaleAt(int slot) =>
        slot == 0 ? 1 : Math.Max(ScaleFloor, FirstScale * Math.Pow(ScaleStep, slot - 1));

    /// <summary>Angle(k): 0 at the centre, FirstAngle at ±1, linear to AngleCap at
    /// ±AngleCapSlot, constant beyond.</summary>
    public static double AngleAt(int slot)
    {
        if (slot == 0) return 0;
        if (slot >= AngleCapSlot) return AngleCap;
        return FirstAngle + (AngleCap - FirstAngle) * (slot - 1) / (AngleCapSlot - 1);
    }

    /// <summary>Opacity(k) = max(OpacityFloor, FirstOpacity − OpacityStep·(k−1)); 1 at the
    /// centre, 0 at the exit slot.</summary>
    public static double OpacityAt(int slot)
    {
        if (slot == 0) return 1;
        if (slot >= ExitSlot) return 0;
        return Math.Max(OpacityFloor, FirstOpacity - OpacityStep * (slot - 1));
    }

    /// <summary>Dim(k) = min(DimMax, DimStep · k).</summary>
    public static double DimAt(int slot) => Math.Min(DimMax, DimStep * slot);

    /// <summary>Y(k) = −ArcRisePerSlot · k.</summary>
    public static double YAt(int slot) => -ArcRisePerSlot * slot;

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
            Opacity: Lerp(Opacities),
            Y: Lerp(Ys));
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
