using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Noctis.Controls;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// MarqueeTextBlock edge fade. The viewport clips the text mid-glyph while it overflows,
/// so a sliver of the next letter sat hard against the edge — the "artifact" the user hit
/// in the mini player, next to the heart button. The playback bar already fades its own
/// marquee's edges for exactly this reason (PlaybackBarView.axaml, 09-14); these pin the
/// same behaviour for the shared control every other view uses.
/// </summary>
public class MarqueeEdgeFadeTests : IDisposable
{
    // The scroll switches are process-wide statics that other view tests render against,
    // so whatever a case here sets has to be put back even when it fails.
    private readonly bool _savedTitleScroll = MarqueeTextBlock.GlobalMiniPlayerTitleScrollEnabled;

    public void Dispose() => MarqueeTextBlock.GlobalMiniPlayerTitleScrollEnabled = _savedTitleScroll;

    private static Border Viewport(MarqueeTextBlock m) =>
        (Border)typeof(MarqueeTextBlock)
            .GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(m)!;

    private static TranslateTransform Transform(MarqueeTextBlock m) =>
        (TranslateTransform)typeof(MarqueeTextBlock)
            .GetField("_transform", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(m)!;

    private static void OnFrame(MarqueeTextBlock m, TimeSpan t) =>
        typeof(MarqueeTextBlock)
            .GetMethod("OnFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(m, new object[] { t });

    private static void SetField(MarqueeTextBlock m, string name, object value) =>
        typeof(MarqueeTextBlock)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(m, value);

    /// <summary>Mounts a marquee in a fixed-width host and settles layout.</summary>
    private static (Window win, MarqueeTextBlock marquee) Mount(string text, double width, bool scrollEnabled = true)
    {
        MarqueeTextBlock.GlobalMiniPlayerTitleScrollEnabled = scrollEnabled;
        var marquee = new MarqueeTextBlock
        {
            Text = text,
            FontSize = 14,
            MaxDisplayWidth = width,
            IsMiniPlayer = true,
        };
        var host = new Border { Width = width, Height = 40, Child = marquee };
        var win = new Window { Width = width + 40, Height = 80, Content = host };
        win.Show();
        win.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        win.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (win, marquee);
    }

    private static (Color first, Color last, IReadOnlyList<IGradientStop> stops) Stops(IBrush? mask)
    {
        var g = Assert.IsAssignableFrom<ILinearGradientBrush>(mask);
        var stops = g.GradientStops.OrderBy(s => s.Offset).ToList();
        return (stops[0].Color, stops[^1].Color, stops);
    }

    [AvaloniaFact]
    public void OverflowingTitle_FadesTheTrailingEdgeInsteadOfCuttingAGlyph()
    {
        var (win, m) = Mount("I Forgot That You Exist (Taylor's Version)", 160);
        try
        {
            var (first, last, stops) = Stops(Viewport(m).OpacityMask);
            // Leading edge is untouched at rest — the title starts flush against it.
            Assert.Equal(byte.MaxValue, first.A);
            // Trailing edge fades out, so the clipped glyph dissolves rather than being
            // sliced off against the button beside it.
            Assert.Equal(0, last.A);
            Assert.True(stops[^2].Offset < 1.0 && stops[^2].Color.A == byte.MaxValue,
                "the fade must be a ramp, not a hard stop");
        }
        finally { win.Close(); }
    }

    [AvaloniaFact]
    public void ScrolledTitle_FadesBothEdges()
    {
        var (win, m) = Mount("I Forgot That You Exist (Taylor's Version)", 160);
        try
        {
            // Drive one frame's worth of lap: the text has moved left, so its head is now
            // cut by the leading edge too.
            SetField(m, "_isRunning", true);
            SetField(m, "_lastFrameTimestamp", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency / 10);
            OnFrame(m, TimeSpan.FromMilliseconds(100));

            Assert.True(Transform(m).X < -0.5, $"expected the text to have moved left, X={Transform(m).X}");
            var (first, last, _) = Stops(Viewport(m).OpacityMask);
            Assert.Equal(0, first.A);
            Assert.Equal(0, last.A);
        }
        finally { win.Close(); }
    }

    [AvaloniaFact]
    public void ShortTitle_KeepsBothEdgesCrisp()
    {
        var (win, m) = Mount("Taylor Swift", 400);
        try
        {
            // Nothing is clipped, so a fade would only dim a perfectly good title.
            Assert.Null(Viewport(m).OpacityMask);
        }
        finally { win.Close(); }
    }

    [AvaloniaFact]
    public void OverflowingTitle_WithScrollDisabled_KeepsBothEdgesCrisp()
    {
        var (win, m) = Mount("I Forgot That You Exist (Taylor's Version)", 160, scrollEnabled: false);
        try
        {
            // The static path ellipsizes inside the viewport and never cuts a glyph, so
            // fading would just wash out the "…".
            Assert.Null(Viewport(m).OpacityMask);
        }
        finally { win.Close(); }
    }
}
