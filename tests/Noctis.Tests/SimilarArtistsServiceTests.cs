using System.Text.Json;
using Noctis.Models;
using Noctis.Services;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The Similar Artists tab's pure parts: the Deezer search must pick the real account
/// among same-name impostors (fans break the tie, like the portrait fetcher), the
/// related list keeps Deezer's order, and rows join to the library by name so an artist
/// the user has opens their page with their own portrait.
/// </summary>
public class SimilarArtistsServiceTests
{
    [Fact]
    public void PickBestMatch_ExactNameOnly_MostFansWins()
    {
        using var doc = JsonDocument.Parse("""
        {"data":[
          {"id":1,"name":"Chase Atlantic","nb_fan":6},
          {"id":2,"name":"Chase Atlantic Tribute","nb_fan":9000000},
          {"id":3,"name":"chase atlantic","nb_fan":2400000}
        ]}
        """);
        Assert.Equal(3, SimilarArtistsService.PickBestMatch(doc.RootElement, "Chase Atlantic"));
        Assert.Equal(0, SimilarArtistsService.PickBestMatch(doc.RootElement, "Nobody"));
    }

    /// <summary>Real "Arcángel" search: the 1.7M-fan account is spelled "Arcangel".</summary>
    [Fact]
    public void PickBestMatch_IgnoresDiacritics()
    {
        using var doc = JsonDocument.Parse("""
        {"data":[
          {"id":14033577,"name":"Arcángel","nb_fan":1683},
          {"id":9216716,"name":"Arcángel & DJ Luian","nb_fan":3589},
          {"id":5536564,"name":"Arcangel","nb_fan":1704853}
        ]}
        """);
        Assert.Equal(5536564, SimilarArtistsService.PickBestMatch(doc.RootElement, "Arcángel"));
        Assert.Equal(5536564, SimilarArtistsService.PickBestMatch(doc.RootElement, "arcangel"));
    }

    [Fact]
    public void ParseRelated_KeepsOrder_PrefersBigPicture_SkipsPlaceholders()
    {
        using var doc = JsonDocument.Parse("""
        {"data":[
          {"id":10,"name":"The Neighbourhood","picture_medium":"https://cdn/m.jpg","picture_big":"https://cdn/b.jpg"},
          {"id":11,"name":"6LACK","picture_big":"https://e-cdns-images.dzcdn.net/images/artist//500x500.jpg","picture_medium":"https://cdn/6lack.jpg"},
          {"id":0,"name":"broken"},
          {"id":12,"name":"","picture_big":"https://cdn/x.jpg"}
        ]}
        """);
        var related = SimilarArtistsService.ParseRelated(doc.RootElement);
        Assert.Equal(new[] { "The Neighbourhood", "6LACK" }, related.Select(r => r.Name));
        Assert.Equal("https://cdn/b.jpg", related[0].PictureUrl);
        Assert.Equal("https://cdn/6lack.jpg", related[1].PictureUrl); // placeholder big → medium
    }

    [Fact]
    public void BuildSimilarRows_JoinsToTheLibraryByName()
    {
        var related = new List<SimilarArtist>
        {
            new() { DeezerId = 1, Name = "the neighbourhood", ImagePath = "deezer-tnbhd.jpg" },
            new() { DeezerId = 2, Name = "Joji", ImagePath = "deezer-joji.jpg" },
        };
        var library = new List<Artist>
        {
            new() { Id = Guid.NewGuid(), Name = "The Neighbourhood", ImagePath = "library-tnbhd.jpg" },
        };
        var rows = ArtistDetailViewModel.BuildSimilarRows(related, library, images: null);

        Assert.True(rows[0].IsInLibrary);
        Assert.Equal("The Neighbourhood", rows[0].Name);        // the library's casing
        Assert.Equal("library-tnbhd.jpg", rows[0].ImagePath);   // the user's portrait, not Deezer's
        Assert.False(rows[1].IsInLibrary);
        Assert.Equal("deezer-joji.jpg", rows[1].ImagePath);
    }
}
