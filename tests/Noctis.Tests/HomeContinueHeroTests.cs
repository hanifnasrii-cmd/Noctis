using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Home's "Continue listening" hero: the " · Album · Year" detail that follows the
/// accent-coloured artist name (HomeViewModel.BuildContinueDetail).
/// </summary>
public class HomeContinueHeroTests
{
    private static Track T(string album = "Un Verano Sin Ti", int year = 0, string releaseDate = "") => new()
    {
        Id = Guid.NewGuid(),
        Title = "Un Ratito",
        Artist = "Bad Bunny",
        Album = album,
        Year = year,
        ReleaseDate = releaseDate,
        Duration = TimeSpan.FromSeconds(176),
    };

    [Fact]
    public void AlbumAndYear_AfterASeparator()
        => Assert.Equal(" · Un Verano Sin Ti · 2022", HomeViewModel.BuildContinueDetail(T(year: 2022)));

    [Fact]
    public void Year_PrefersTheReleaseDate()
        => Assert.Equal(" · Un Verano Sin Ti · 2022", HomeViewModel.BuildContinueDetail(T(year: 2021, releaseDate: "2022-05-06")));

    [Fact]
    public void UnknownYear_IsSkipped()
        => Assert.Equal(" · Un Verano Sin Ti", HomeViewModel.BuildContinueDetail(T()));

    [Fact]
    public void UnknownAlbum_LeavesJustTheYear()
        => Assert.Equal(" · 2022", HomeViewModel.BuildContinueDetail(T(album: "", year: 2022)));

    [Fact]
    public void NothingKnown_IsEmpty()
        => Assert.Equal(string.Empty, HomeViewModel.BuildContinueDetail(T(album: "")));
}
