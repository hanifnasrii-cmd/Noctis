using Noctis.Helpers;
using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Pure geometry + step logic behind the five-card Cover Flow carousel: the slot poses the
/// view interpolates during a skip, and how the view-model decides how far the row moved.
/// </summary>
public class CoverFlowCarouselGeometryTests
{
    [Fact]
    public void CentreSlot_IsTheIdentityPose()
    {
        var p = CoverFlowCarouselGeometry.At(0);
        Assert.Equal(0, p.X);
        Assert.Equal(1, p.Scale);
        Assert.Equal(0, p.AngleY);
        Assert.Equal(0, p.Dim);
        Assert.Equal(1, p.Opacity);
    }

    [Fact]
    public void SideSlots_MirrorAndTiltTowardTheCentre()
    {
        var l1 = CoverFlowCarouselGeometry.At(-1);
        var r1 = CoverFlowCarouselGeometry.At(1);
        var r2 = CoverFlowCarouselGeometry.At(2);

        Assert.Equal(-r1.X, l1.X);
        Assert.Equal(-r1.AngleY, l1.AngleY);
        Assert.Equal(r1.Scale, l1.Scale);
        // Left cards tilt with a NEGATIVE AngleY (right edge nearer), right cards positive.
        Assert.True(l1.AngleY < 0);
        Assert.True(r1.AngleY > 0);
        // Spec: ±1 ≈ 85% at ~15–20°, ±2 ≈ 70% at ~25–30°, dimmer and a little transparent.
        Assert.Equal(CoverFlowCarouselGeometry.FirstScale, r1.Scale, 6);
        Assert.Equal(CoverFlowCarouselGeometry.FirstAngle, r1.AngleY, 6);
        Assert.Equal(CoverFlowCarouselGeometry.FirstScale * CoverFlowCarouselGeometry.ScaleStep, r2.Scale, 6);
        Assert.InRange(r2.AngleY, CoverFlowCarouselGeometry.FirstAngle, CoverFlowCarouselGeometry.AngleCap);
        Assert.True(r2.Dim > r1.Dim);
        Assert.True(r2.Opacity < r1.Opacity);
        Assert.True(r2.X > r1.X);
    }

    [Fact]
    public void ExitSlot_IsTransparent_AndPositionsClamp()
    {
        Assert.Equal(2, CoverFlowCarouselGeometry.SideSlots);
        Assert.True(CoverFlowCarouselGeometry.At(2).Opacity >= CoverFlowCarouselGeometry.OpacityFloor, "the second card on a side is a real, visible card");
        Assert.Equal(0, CoverFlowCarouselGeometry.At(3).Opacity);
        Assert.Equal(CoverFlowCarouselGeometry.At(3), CoverFlowCarouselGeometry.At(7));
        Assert.Equal(CoverFlowCarouselGeometry.At(-3), CoverFlowCarouselGeometry.At(-9));
    }

    [Fact]
    public void SlideDuration_GrowsWithTheDistanceJumped()
    {
        var one = CoverFlowCarouselGeometry.SlideDurationFor(1);
        Assert.Equal(CoverFlowCarouselGeometry.SlideDuration, one);
        Assert.Equal(one, CoverFlowCarouselGeometry.SlideDurationFor(-1));
        Assert.Equal(one, CoverFlowCarouselGeometry.SlideDurationFor(0));
        Assert.True(CoverFlowCarouselGeometry.SlideDurationFor(4) > one);
        Assert.True(CoverFlowCarouselGeometry.SlideDurationFor(2) > one);
        Assert.Equal(CoverFlowCarouselGeometry.SlideDurationFor(2), CoverFlowCarouselGeometry.SlideDurationFor(-2));
        Assert.True(CoverFlowCarouselGeometry.SlideDurationFor(2) <= TimeSpan.FromMilliseconds(1000), "a two-slot jump still lands well under a second");
    }

    [Fact]
    public void Row_RecedesAndCompressesOutward()
    {
        // Classic depth: each step outward is SHORTER than the last (geometric spacing),
        // the card smaller (to a floor), more tilted (to a cap) and dimmer.
        Assert.Equal(CoverFlowCarouselGeometry.CenterGap, CoverFlowCarouselGeometry.At(1).X, 6);
        var prevStep = double.MaxValue;
        for (var s = 1; s <= CoverFlowCarouselGeometry.SideSlots; s++)
        {
            var inner = CoverFlowCarouselGeometry.At(s - 1);
            var outer = CoverFlowCarouselGeometry.At(s);
            var step = outer.X - inner.X;
            Assert.True(step > 0, $"slot {s} sits further out than slot {s - 1}");
            Assert.True(step < prevStep, $"the step to slot {s} ({step}) is shorter than the one before ({prevStep})");
            if (s >= 2) Assert.Equal(CoverFlowCarouselGeometry.StepShrink, step / prevStep, 6);
            prevStep = step;
            Assert.True(outer.Scale <= inner.Scale && outer.Scale >= CoverFlowCarouselGeometry.ScaleFloor);
            Assert.True(outer.AngleY >= inner.AngleY && outer.AngleY <= CoverFlowCarouselGeometry.AngleCap);
            Assert.True(outer.Dim >= inner.Dim);
            Assert.True(outer.Opacity <= inner.Opacity && outer.Opacity >= CoverFlowCarouselGeometry.OpacityFloor);
            Assert.True(CoverFlowCarouselGeometry.ZIndexAt(s) < CoverFlowCarouselGeometry.ZIndexAt(s - 1));
        }
        Assert.Equal(CoverFlowCarouselGeometry.AngleCap, CoverFlowCarouselGeometry.At(CoverFlowCarouselGeometry.AngleCapSlot).AngleY, 6);
        Assert.Equal(CoverFlowCarouselGeometry.AngleCap, CoverFlowCarouselGeometry.At(CoverFlowCarouselGeometry.SideSlots).AngleY, 6);
        // Reference mockup: the ±2 step is roughly half the ±1 step (measured 0.56-0.60).
        var first = CoverFlowCarouselGeometry.At(1).X;
        var last = CoverFlowCarouselGeometry.At(2).X - CoverFlowCarouselGeometry.At(1).X;
        Assert.InRange(last / first, 0.45, 0.65);
    }

    [Fact]
    public void FloatArc_LiftsOuterCardsAFewPxPerSlot_Mirrored()
    {
        Assert.Equal(0, CoverFlowCarouselGeometry.At(0).Y);
        for (var s = 1; s <= CoverFlowCarouselGeometry.SideSlots; s++)
        {
            Assert.Equal(-CoverFlowCarouselGeometry.ArcRisePerSlot * s, CoverFlowCarouselGeometry.At(s).Y, 6);
            Assert.Equal(CoverFlowCarouselGeometry.At(s).Y, CoverFlowCarouselGeometry.At(-s).Y, 6);
        }
        // Fractional positions ride the arc too (the slide animates Y with X).
        Assert.Equal(-CoverFlowCarouselGeometry.ArcRisePerSlot * 1.5, CoverFlowCarouselGeometry.At(1.5).Y, 6);
    }

    [Fact]
    public void FractionalPositions_InterpolateBetweenSlots()
    {
        var a = CoverFlowCarouselGeometry.At(1);
        var b = CoverFlowCarouselGeometry.At(2);
        var mid = CoverFlowCarouselGeometry.At(1.5);
        Assert.Equal((a.X + b.X) / 2, mid.X, 6);
        Assert.Equal((a.Scale + b.Scale) / 2, mid.Scale, 6);
        Assert.Equal((a.AngleY + b.AngleY) / 2, mid.AngleY, 6);
        Assert.Equal((a.Dim + b.Dim) / 2, mid.Dim, 6);
    }

    [Fact]
    public void ZOrder_FallsWithDistance_EvenMidSlide()
    {
        Assert.True(CoverFlowCarouselGeometry.ZIndexAt(0) > CoverFlowCarouselGeometry.ZIndexAt(1));
        Assert.True(CoverFlowCarouselGeometry.ZIndexAt(-1) > CoverFlowCarouselGeometry.ZIndexAt(2));
        Assert.True(CoverFlowCarouselGeometry.ZIndexAt(0.5) > CoverFlowCarouselGeometry.ZIndexAt(1.5));
    }

    [Fact]
    public void Ease_IsEaseOut()
    {
        Assert.Equal(0, CoverFlowCarouselGeometry.Ease(0));
        Assert.Equal(1, CoverFlowCarouselGeometry.Ease(1));
        Assert.True(CoverFlowCarouselGeometry.Ease(0.5) > 0.5, "ease-out covers most of the distance early");
        Assert.True(CoverFlowCarouselGeometry.Ease(0.25) < CoverFlowCarouselGeometry.Ease(0.5));
    }

    [Fact]
    public void StepBetween_FindsTheNewCentreAmongTheOldNeighbours()
    {
        var c = new Track { Title = "c" };
        var p1 = new Track { Title = "p1" };
        var p2 = new Track { Title = "p2" };
        var n1 = new Track { Title = "n1" };
        var n2 = new Track { Title = "n2" };
        var other = new Track { Title = "x" };

        Assert.Equal(1, CoverFlowViewModel.StepBetween(c, n1, p1, p2, n1, n2));
        Assert.Equal(2, CoverFlowViewModel.StepBetween(c, n2, p1, p2, n1, n2));
        Assert.Equal(-1, CoverFlowViewModel.StepBetween(c, p1, p1, p2, n1, n2));
        Assert.Equal(-2, CoverFlowViewModel.StepBetween(c, p2, p1, p2, n1, n2));
        Assert.Equal(0, CoverFlowViewModel.StepBetween(c, other, p1, p2, n1, n2));
        Assert.Equal(0, CoverFlowViewModel.StepBetween(c, c, p1, p2, n1, n2));
        Assert.Equal(0, CoverFlowViewModel.StepBetween(c, null, p1, p2, n1, n2));
        var p3 = new Track { Title = "p3" }; var n4 = new Track { Title = "n4" };
        Assert.Equal(-3, CoverFlowViewModel.StepBetween(c, p3, p1, p2, n1, n2, p3, null, null, n4));
        Assert.Equal(4, CoverFlowViewModel.StepBetween(c, n4, p1, p2, n1, n2, p3, null, null, n4));
    }
}
