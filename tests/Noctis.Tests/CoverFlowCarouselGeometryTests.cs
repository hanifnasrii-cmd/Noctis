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
        Assert.InRange(r1.Scale, 0.84, 0.86);
        Assert.InRange(r1.AngleY, 15, 20);
        Assert.InRange(r2.Scale, 0.69, 0.71);
        Assert.InRange(r2.AngleY, 25, 30);
        Assert.True(r2.Dim > r1.Dim);
        Assert.True(r2.Opacity < r1.Opacity);
        Assert.True(r2.X > r1.X);
    }

    [Fact]
    public void ExitSlot_IsTransparent_AndPositionsClamp()
    {
        Assert.Equal(0, CoverFlowCarouselGeometry.At(3).Opacity);
        Assert.Equal(CoverFlowCarouselGeometry.At(3), CoverFlowCarouselGeometry.At(7));
        Assert.Equal(CoverFlowCarouselGeometry.At(-3), CoverFlowCarouselGeometry.At(-9));
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
    }
}
