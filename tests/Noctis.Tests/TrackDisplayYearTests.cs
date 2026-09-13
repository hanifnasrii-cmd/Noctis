using Noctis.Models;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The lyrics page prints a year, the album page prints the release date; both must
/// come from the same tag or a file whose YEAR and RELEASEDATE disagree (a 2025
/// deluxe re-issue over an original 2024-11-01 release) shows two different years.
/// </summary>
public class TrackDisplayYearTests
{
    [Theory]
    [InlineData("2024-11-01", 2025, 2024)]
    [InlineData("2014-10-27T07:00:00Z", 2015, 2014)]
    [InlineData("2024/11/29", 2025, 2024)]
    public void DisplayYear_PrefersTheReleaseDateYear_LikeTheAlbumPage(string releaseDate, int yearTag, int expected)
    {
        var track = new Track { Year = yearTag, ReleaseDate = releaseDate };
        Assert.Equal(expected, track.DisplayYear);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    public void DisplayYear_FallsBackToTheYearTag_WhenNoParseableReleaseDate(string releaseDate)
    {
        var track = new Track { Year = 2017, ReleaseDate = releaseDate };
        Assert.Equal(2017, track.DisplayYear);
    }

    [Fact]
    public void DisplayYear_IsZeroForUntaggedFiles_SoTheMetadataLineSkipsIt()
    {
        Assert.Equal(0, new Track().DisplayYear);
    }
}
