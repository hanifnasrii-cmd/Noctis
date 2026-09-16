using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Noctis.Helpers;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Cascade layout slide (09-15): a skip moves every cover one slot along the path with the
/// same eased slide as the carousel, instead of snapping. Pins the path geometry and that
/// each card starts at the slot it came from.
/// </summary>
public class CoverFlowCascadeSlideTests
{
    [Fact]
    public void Path_MatchesTheMockupSlots_AndExitsAreTransparent()
    {
        var centre = CoverFlowCascadeGeometry.At(0);
        Assert.Equal((520d, 268d, 480d, 0d), (centre.Left, centre.Top, centre.Size, centre.Angle));
        Assert.Equal(0, centre.Dim);
        var m1 = CoverFlowCascadeGeometry.At(-1);
        Assert.Equal((432d, 76d, 260d, -20d), (m1.Left, m1.Top, m1.Size, m1.Angle));
        var p1 = CoverFlowCascadeGeometry.At(1);
        Assert.Equal((427d, 685d, 250d, 25d), (p1.Left, p1.Top, p1.Size, p1.Angle));
        Assert.Equal(0, CoverFlowCascadeGeometry.At(-4).Opacity);
        Assert.Equal(0, CoverFlowCascadeGeometry.At(4).Opacity);
        Assert.Equal(CoverFlowCascadeGeometry.At(4), CoverFlowCascadeGeometry.At(9));
        // Halfway between +1 and the centre: halfway in every channel.
        var mid = CoverFlowCascadeGeometry.At(0.5);
        Assert.Equal((centre.Left + p1.Left) / 2, mid.Left, 6);
        Assert.Equal((centre.Size + p1.Size) / 2, mid.Size, 6);
        Assert.Equal((centre.Angle + p1.Angle) / 2, mid.Angle, 6);
        Assert.Equal(15, CoverFlowCascadeGeometry.ZIndexAt(0));
        Assert.True(CoverFlowCascadeGeometry.ZIndexAt(1) > CoverFlowCascadeGeometry.ZIndexAt(2));
    }

    [AvaloniaFact]
    public void Skip_SlidesEveryCascadeCardFromTheSlotItCameFrom()
    {
        var view = new CoverFlowView();
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var centre = view.FindControl<Border>("CascadeCenterCover")!;
        var next1 = view.FindControl<Border>("CascadeSlotP1")!;
        // At rest every card sits on its mockup slot, rotating about its centre.
        Assert.Equal(520, Canvas.GetLeft(centre));
        Assert.Equal(480, centre.Width);
        Assert.Equal(25, ((RotateTransform)next1.RenderTransform!).Angle);
        Assert.Equal(0.08, view.FindControl<Border>("CascadeWashP1")!.Opacity, 3);

        // Forward one: the new playing cover starts where the +1 card was (small, tilted,
        // down-left) and eases up into the big straight slot; the new +3 fades in from +4.
        view.OnCarouselShifted(null, 1);
        view.ApplySlideFrame(0);
        Assert.Equal(1, view.CascadePositionOf(0), 1);
        Assert.Equal(250, centre.Width, 0.5);
        Assert.Equal(25, ((RotateTransform)centre.RenderTransform!).Angle, 0.5);
        Assert.Equal(0, view.FindControl<Border>("CascadeSlotP3")!.Opacity);

        view.ApplySlideFrame(0.5);
        Assert.Equal(0.5, view.CascadePositionOf(0), 1);
        Assert.InRange(centre.Width, 300, 430);
        Assert.InRange(((RotateTransform)centre.RenderTransform!).Angle, 10, 15);

        view.ApplySlideFrame(1);
        Assert.Equal(520, Canvas.GetLeft(centre), 0.5);
        Assert.Equal(480, centre.Width, 0.5);
        Assert.Equal(0, ((RotateTransform)centre.RenderTransform!).Angle, 0.5);
        Assert.Equal(1, view.FindControl<Border>("CascadeSlotP3")!.Opacity);

        // Back one: the new playing cover comes down from the −1 slot.
        view.OnCarouselShifted(null, -1);
        view.ApplySlideFrame(0);
        Assert.Equal(-1, view.CascadePositionOf(0), 1);
        Assert.Equal(-20, ((RotateTransform)centre.RenderTransform!).Angle, 0.5);
    }
}
