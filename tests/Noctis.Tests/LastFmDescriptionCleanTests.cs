using Noctis.Services;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Discord (aaron, 09-15): every album description ended in " ." — Last.fm keeps the
/// sentence period outside its "Read more" anchor, and the cleaner used to strip the
/// phrase before the tags, stranding that period.
/// </summary>
public class LastFmDescriptionCleanTests
{
    private const string Raw =
        "Donda debuted at number one, becoming one of the biggest debuts on Spotify and Apple Music. " +
        "<a href=\"https://www.last.fm/music/Kanye+West/Donda\">Read more on Last.fm</a>.";

    [Fact]
    public void Summary_ReadMoreAnchorOutsidePeriod_EndsWithOneDot()
    {
        var cleaned = LastFmService.CleanAlbumSummary(Raw);

        Assert.NotNull(cleaned);
        Assert.EndsWith("Spotify and Apple Music.", cleaned);
        Assert.DoesNotContain(". .", cleaned);
    }

    [Fact]
    public void Content_ParagraphsKept_NoOrphanDot()
    {
        var raw = "<p>First paragraph ends here.</p>\n<p>" + Raw + "</p>";
        var cleaned = LastFmService.CleanAlbumContent(raw);

        Assert.NotNull(cleaned);
        Assert.EndsWith("Spotify and Apple Music.", cleaned);
        Assert.Contains("First paragraph ends here.\n", cleaned);
        Assert.DoesNotContain(". .", cleaned);
    }

    [Fact]
    public void ScrubOrphanPeriods_RepairsCachedText()
    {
        Assert.Equal("Music.", LastFmService.ScrubOrphanPeriods("Music. ."));
        Assert.Equal("Music.\n\nNext.", LastFmService.ScrubOrphanPeriods("Music. .\n\nNext. ."));
        Assert.Equal("Ends with 3.5 . really", LastFmService.ScrubOrphanPeriods("Ends with 3.5 . really"));
        Assert.Null(LastFmService.ScrubOrphanPeriods(null));
    }
}
