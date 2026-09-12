using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Noctis.Controls;
using Noctis.Helpers;
using SkiaSharp;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Liquid Glass frost: the Skia backdrop blur measurably softens what lies under a
/// panel (and nothing outside its rounded clip), and the panel's tint/active plumbing
/// follows the app-wide flag.
/// </summary>
public class GlassPanelTests
{
    private const int W = 200, H = 120;

    /// <summary>Hard 8px black/white vertical stripes: any blur shows up as lost contrast.</summary>
    private static SKSurface Stripes()
    {
        var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Black);
        using var white = new SKPaint { Color = SKColors.White };
        for (var x = 0; x < W; x += 16) c.DrawRect(new SKRect(x, 0, x + 8, H), white);
        return surface;
    }

    /// <summary>Peak-to-peak luminance along one pixel row within [x0,x1).</summary>
    private static int RowContrast(SKSurface surface, int y, int x0, int x1)
    {
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        int min = 255, max = 0;
        for (var x = x0; x < x1; x++)
        {
            var v = bmp.GetPixel(x, y).Red;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return max - min;
    }

    [Fact]
    public void Draw_SoftensStripesInsideTheClip()
    {
        using var surface = Stripes();
        var rect = new SKRect(40, 20, 160, 100);
        var rr = new SKRoundRect(rect, 20);
        Assert.Equal(255, RowContrast(surface, 60, 60, 140));

        Assert.True(GlassBlur.Draw(surface.Canvas, surface, rect, rr, sigma: 8));

        // Stripes are 16px period; σ=8 flattens them to a gentle ripple.
        Assert.InRange(RowContrast(surface, 60, 60, 140), 0, 60);
    }

    [Fact]
    public void Draw_LeavesPixelsOutsideTheClipUntouched()
    {
        using var surface = Stripes();
        var rect = new SKRect(40, 20, 160, 100);
        var rr = new SKRoundRect(rect, 20);
        GlassBlur.Draw(surface.Canvas, surface, rect, rr, sigma: 8);

        // Row above the panel, and the rounded corner pixel just inside the bounding rect.
        Assert.Equal(255, RowContrast(surface, 5, 0, W));
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        var corner = bmp.GetPixel(41, 21).Red; // inside rect, outside the r=20 arc
        Assert.True(corner is 0 or 255, $"corner pixel was blended: {corner}");
    }

    [Fact]
    public void Draw_LargerSigmaBlursMore()
    {
        using var a = Stripes();
        using var b = Stripes();
        var rect = new SKRect(40, 20, 160, 100);
        GlassBlur.Draw(a.Canvas, a, rect, new SKRoundRect(rect, 0), sigma: 2);
        GlassBlur.Draw(b.Canvas, b, rect, new SKRoundRect(rect, 0), sigma: 10);
        Assert.True(RowContrast(a, 60, 60, 140) > RowContrast(b, 60, 60, 140));
    }

    [Fact]
    public void Draw_PathClip_FollowsARotatedOutline()
    {
        // A tilted host (Cover Flow side card) hands the blur its rounded rect run through
        // the render matrix. Pixels inside the rotated shape soften; a corner of the
        // axis-aligned bounding box that lies OUTSIDE the rotated shape stays crisp.
        using var surface = Stripes();
        using var path = new SKPath();
        path.AddRect(new SKRect(60, 50, 180, 110));
        path.Transform(SKMatrix.CreateRotationDegrees(30, 120, 80));
        var box = path.Bounds; // ~ (53,24)-(187,136): its top-left corner is outside the shape
        var cornerY = (int)box.Top + 2;
        var before = RowContrast(surface, cornerY, (int)box.Left + 2, (int)box.Left + 12);
        Assert.True(before > 200);
        Assert.True(GlassBlur.Draw(surface.Canvas, surface, box, path, sigma: 6));
        Assert.Equal(before, RowContrast(surface, cornerY, (int)box.Left + 2, (int)box.Left + 12));
        Assert.True(RowContrast(surface, 80, 100, 140) < before / 4, "inside the rotated clip should soften");
    }

    [AvaloniaFact] // same UI thread as the panel tests: AppGlass is process-wide state
    public void AppGlass_PublishesStateAndRaisesChanged()
    {
        var raised = 0;
        EventHandler h = (_, _) => raised++;
        AppGlass.Changed += h;
        try
        {
            AppGlass.Set(true, Colors.Red, Colors.Blue);
            Assert.True(AppGlass.IsActive);
            Assert.Equal(Colors.Red, AppGlass.SurfaceTint);
            Assert.Equal(Colors.Blue, AppGlass.SidebarTint);
            AppGlass.Clear();
            Assert.False(AppGlass.IsActive);
            Assert.Equal(Colors.Red, AppGlass.SurfaceTint);
            Assert.Equal(2, raised);
        }
        finally
        {
            AppGlass.Changed -= h;
            AppGlass.Clear();
        }
    }

    [AvaloniaFact]
    public void Panel_FollowsAppGlassUnlessDetached()
    {
        var panel = new GlassPanel();
        try
        {
            AppGlass.Set(true, Colors.Black, Colors.Black);
            Assert.True(panel.EffectiveGlassActive);
            panel.UseAppGlass = false;
            Assert.False(panel.EffectiveGlassActive);
            panel.IsGlassActive = true;
            Assert.True(panel.EffectiveGlassActive);
        }
        finally { AppGlass.Clear(); }
    }

    [AvaloniaFact]
    public void Panel_TintDefaultsToTheBackgroundBrush()
    {
        var panel = new GlassPanel
        {
            Background = new SolidColorBrush(Color.Parse("#202020"), 0.4),
        };
        var (color, opacity) = panel.ResolveTint();
        Assert.Equal(Color.Parse("#202020"), color);
        Assert.Equal(0.4, opacity, 3);

        panel.GlassTintOpacity = 0.55;
        panel.GlassTint = Colors.Navy;
        (color, opacity) = panel.ResolveTint();
        Assert.Equal(Colors.Navy, color);
        Assert.Equal(0.55, opacity, 3);
    }

    [AvaloniaFact]
    public void Panel_RendersOffAndOnWithoutThrowing()
    {
        var panel = new GlassPanel
        {
            Width = 100, Height = 40, CornerRadius = new CornerRadius(12),
            Background = Brushes.Gray, UseAppGlass = false,
        };
        var window = new Avalonia.Controls.Window { Content = panel, Width = 200, Height = 100 };
        window.Show();
        panel.IsGlassActive = true;
        panel.IsGlassActive = false;
        window.Close();
    }
}
