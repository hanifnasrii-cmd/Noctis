using System.Linq;
using Noctis.Services.LyricsStudio;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

public class LyricsStudioCountsTests
{
    [Fact]
    public void BuildLyricsStudioCounts_BucketsEveryFormat_InTileOrder()
    {
        var formats = new[]
        {
            LyricsFormat.Elrc, LyricsFormat.Elrc, LyricsFormat.Lrc, LyricsFormat.Plain,
            LyricsFormat.None, LyricsFormat.None, LyricsFormat.None,
        };
        var counts = SettingsViewModel.BuildLyricsStudioCounts(formats);
        Assert.Equal(new[] { 2, 1, 1, 3 }, counts.Select(c => c.Count));
        Assert.Equal(new[] { "ELRC", "LRC", "", "" }, counts.Select(c => c.Tag));
        Assert.All(counts, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
    }
}
