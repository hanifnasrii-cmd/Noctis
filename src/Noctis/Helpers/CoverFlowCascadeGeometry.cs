using System;

namespace Noctis.Helpers;

/// <summary>One card's place on the Cascade's 1000×1000 design canvas at a (fractional) slot.</summary>
/// <param name="Left">Canvas.Left of the card's top-left corner.</param>
/// <param name="Top">Canvas.Top of the card's top-left corner.</param>
/// <param name="Size">Side of the square card.</param>
/// <param name="Angle">Rotation about the card centre, degrees.</param>
/// <param name="Dim">0..1 black wash.</param>
/// <param name="Opacity">Card opacity: the ±4 entry/exit slots are transparent so a card
/// joining or leaving the path fades rather than pops.</param>
public readonly record struct CascadePose(double Left, double Top, double Size, double Angle, double Dim, double Opacity);

/// <summary>
/// Pure slot geometry for the Cascade layout: the covers sit on ONE ordered path (history
/// climbing up-left above the playing cover, up-next falling down-left below it), so a skip
/// moves every card one slot along the path. Slots −3..+3 are the on-canvas positions from
/// the mockup; −4/+4 continue the path off-canvas and are where a card enters from / exits
/// to. <see cref="At"/> lerps between neighbouring slots so the view can slide a card from
/// the slot it came from to its own, mirroring the carousel's slide.
/// </summary>
public static class CoverFlowCascadeGeometry
{
    public const int SideSlots = 3;
    public const int ExitSlot = SideSlots + 1;

    // Index = slot + ExitSlot (so 0 → slot −4, 4 → slot 0, 8 → slot +4).
    private static readonly CascadePose[] Slots =
    {
        new(-250, -330, 190, -10, 0.40, 0),   // −4 entry/exit, off the top-left
        new( -31,  -96, 208, -18, 0.30, 1),   // −3
        new( 198,  -65, 250, -33, 0.18, 1),   // −2
        new( 432,   76, 260, -20, 0.08, 1),   // −1
        new( 520,  268, 480,   0, 0.00, 1),   //  0 playing
        new( 427,  685, 250,  25, 0.08, 1),   // +1
        new( 209,  832, 270, -18, 0.18, 1),   // +2
        new( -52,  873, 250,  12, 0.30, 1),   // +3
        new(-300, 1100, 230,  20, 0.40, 0),   // +4 entry/exit, off the bottom-left
    };

    /// <summary>Pose for a signed, possibly fractional, slot; clamped to ±<see cref="ExitSlot"/>.</summary>
    public static CascadePose At(double slot)
    {
        var s = Math.Clamp(slot, -ExitSlot, ExitSlot) + ExitSlot;
        var i = (int)Math.Floor(s);
        if (i >= Slots.Length - 1) return Slots[^1];
        var t = s - i;
        var a = Slots[i];
        var b = Slots[i + 1];
        double L(double x, double y) => x + (y - x) * t;
        return new CascadePose(L(a.Left, b.Left), L(a.Top, b.Top), L(a.Size, b.Size), L(a.Angle, b.Angle), L(a.Dim, b.Dim), L(a.Opacity, b.Opacity));
    }

    /// <summary>Draw order from the mockup: playing cover on top (15), then 6/4/2 outward.</summary>
    public static int ZIndexAt(double slot)
    {
        var d = Math.Abs(slot);
        if (d < 0.5) return 15;
        return Math.Max(0, 8 - (int)Math.Round(d * 2));
    }
}
