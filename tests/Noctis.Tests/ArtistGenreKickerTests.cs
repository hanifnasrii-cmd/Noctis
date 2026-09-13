using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>The artist hero kicker ("HIP HOP"): dominant library genre tag, upper-cased.</summary>
public class ArtistGenreKickerTests
{
    private static Track T(string? genre) => new() { Title = "t", Genre = genre ?? string.Empty };

    [Fact]
    public void DominantGenre_IsTheMostCommonTag_UpperCased_CaseInsensitively()
    {
        var songs = new[] { T("alternative"), T("Alternative"), T("Pop"), T("ALTERNATIVE"), T("pop") };
        Assert.Equal("ALTERNATIVE", ArtistDetailViewModel.DominantGenre(songs));
    }

    [Fact]
    public void DominantGenre_TiesBreakAlphabetically_AndBlanksAreIgnored()
    {
        var songs = new[] { T("Rock"), T("Pop"), T(""), T("  "), T(null) };
        Assert.Equal("POP", ArtistDetailViewModel.DominantGenre(songs));
    }

    [Fact]
    public void DominantGenre_IsNullWhenNothingIsTagged_SoTheKickerHides()
    {
        Assert.Null(ArtistDetailViewModel.DominantGenre(new[] { T(""), T(null) }));
        Assert.Null(ArtistDetailViewModel.DominantGenre(Array.Empty<Track>()));
    }
}
