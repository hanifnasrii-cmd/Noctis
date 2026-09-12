using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Noctis.Controls;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// HighlightTextBlock built inline Runs for every label even with no search active, and
/// rebuilt them again for each font property the styles set on attach. Every grid tile and
/// list row paid that: 3.4ms of a realized Artists row, part of the wheel-glide hitch on each
/// new row (09-12). With nothing to highlight it now sets plain Text.
/// </summary>
public class HighlightTextBlockFastPathTests
{
    [AvaloniaFact]
    public void NoQuery_UsesPlainText_NoRuns()
    {
        var block = new HighlightTextBlock { DisplayText = "Bad Bunny" };
        Assert.Equal("Bad Bunny", block.Text);
        Assert.True(block.Inlines is null || block.Inlines.Count == 0, "no runs expected without a query");
    }

    [AvaloniaFact]
    public void QueryThatDoesNotMatch_StaysOnPlainText()
    {
        var block = new HighlightTextBlock { DisplayText = "Bad Bunny", HighlightText = "zzz" };
        Assert.Equal("Bad Bunny", block.Text);
        Assert.True(block.Inlines is null || block.Inlines.Count == 0);
    }

    [AvaloniaFact]
    public void MatchingQuery_BuildsRuns_AndClearingItGoesBackToText()
    {
        var block = new HighlightTextBlock { DisplayText = "Bad Bunny", HighlightText = "bun" };
        Assert.NotNull(block.Inlines);
        Assert.True(block.Inlines!.Count >= 2, $"expected split runs, got {block.Inlines.Count}");

        block.HighlightText = "";
        Assert.Empty(block.Inlines);
        Assert.Equal("Bad Bunny", block.Text);
    }

    [AvaloniaFact]
    public void Explicit_StillGetsItsBadgeInline()
    {
        var block = new HighlightTextBlock { DisplayText = "Song", IsExplicit = true };
        Assert.NotNull(block.Inlines);
        Assert.Contains(block.Inlines!, i => i is Avalonia.Controls.Documents.InlineUIContainer);
    }
}
