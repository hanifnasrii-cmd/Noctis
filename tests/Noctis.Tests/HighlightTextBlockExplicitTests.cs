using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Noctis.Controls;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Favorites showed "VolvíVolví" for an explicit single until something re-laid the
/// tile out. Evidence: HighlightTextBlock writes plain titles to TextBlock.Text (the
/// 09-12 fast path) and switches to Inlines once IsExplicit lands; the two must not
/// both render. Lengths are compared relatively: the layout counts an end-of-line
/// character per line on top of the text, so "Volví" measures 6 and with the badge's
/// placeholder 7 — a doubled title would measure 12.
/// </summary>
public class HighlightTextBlockExplicitTests
{
    private static int RenderedLength(TextBlock tb)
        => tb.TextLayout.TextLines.Sum(l => l.Length);

    private static void Relayout(Window win, TextBlock tb)
    {
        tb.InvalidateMeasure();
        Dispatcher.UIThread.RunJobs();
        win.UpdateLayout();
    }

    [AvaloniaFact]
    public void PlainTitle_ThenExplicit_RendersTheTitleOnce()
    {
        var tb = new HighlightTextBlock { DisplayText = "Volví" };
        var win = new Window { Width = 400, Height = 100, Content = tb };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var plain = RenderedLength(tb);

        tb.IsExplicit = true; // bindings land in this order on a realized tile
        Relayout(win, tb);

        Assert.Equal(plain + 1, RenderedLength(tb)); // + badge placeholder, not + "Volví"
    }

    [AvaloniaFact]
    public void ExplicitTitle_ThenPlain_RendersTheTitleOnce()
    {
        var tb = new HighlightTextBlock { DisplayText = "Volví", IsExplicit = true };
        var win = new Window { Width = 400, Height = 100, Content = tb };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var explicitLength = RenderedLength(tb);

        tb.IsExplicit = false;
        Relayout(win, tb);

        Assert.Equal(explicitLength - 1, RenderedLength(tb));
    }

    [AvaloniaFact]
    public void ExplicitTitle_ReboundToAnotherTitle_RendersTheNewTitleOnce()
    {
        // Recycled tile: DisplayText changes while IsExplicit stays on.
        var tb = new HighlightTextBlock { DisplayText = "Volví", IsExplicit = true };
        var win = new Window { Width = 400, Height = 100, Content = tb };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var five = RenderedLength(tb);

        tb.DisplayText = "Un Ratito"; // 9 chars
        Relayout(win, tb);

        Assert.Equal(five + 4, RenderedLength(tb));
    }
}
