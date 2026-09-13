using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Home's right-hand "Albums" rail (the last played album as a card with its tracks)
/// and the "Last Played" chart (newest distinct tracks from the play history).
/// </summary>
public class HomeRecentRailTests
{
    private static Album A(string name, params Track[] tracks) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Tracks = tracks.ToList(),
    };

    private static Track T(string title, int disc = 1, int number = 1) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        DiscNumber = disc,
        TrackNumber = number,
    };

    [Fact]
    public void Rail_FeaturesNewestAlbum()
    {
        var a = A("a");
        var b = A("b");

        var rail = HomeViewModel.BuildRecentRail(new[] { a, b }, maxTracks: 8);

        Assert.Same(a, rail.Featured);
    }

    [Fact]
    public void Rail_TracksOrderedByDiscThenNumber_AndCapped()
    {
        var album = A("a",
            T("d2-1", disc: 2, number: 1),
            T("d1-3", disc: 1, number: 3),
            T("d1-1", disc: 1, number: 1),
            T("d1-2", disc: 1, number: 2));

        var rail = HomeViewModel.BuildRecentRail(new[] { album }, maxTracks: 3);

        Assert.Equal(new[] { "d1-1", "d1-2", "d1-3" }, rail.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Rail_EmptyRecent_YieldsNothing()
    {
        var rail = HomeViewModel.BuildRecentRail(Array.Empty<Album>(), maxTracks: 8);

        Assert.Null(rail.Featured);
        Assert.Empty(rail.Tracks);
    }

    [Fact]
    public void LastPlayed_KeepsNewestPositionOfRepeats_AndCaps()
    {
        var a = T("a");
        var b = T("b");
        var c = T("c");
        // newest first: a, b, a(again), c
        var history = new[] { a, b, a, c };

        var rows = HomeViewModel.BuildLastPlayed(history, max: 2);

        Assert.Equal(new[] { a, b }, rows);
    }

    [Fact]
    public void LastPlayed_DistinctById()
    {
        var a = T("a");
        var b = T("b");
        var c = T("c");

        var rows = HomeViewModel.BuildLastPlayed(new[] { a, b, a, c }, max: 6);

        Assert.Equal(new[] { a, b, c }, rows);
    }

    [Fact]
    public void LastPlayedRow_HasNoPodium()
    {
        var row = new TopSongRow { Track = T("a"), Rank = 1, IsLastPlayed = true };
        var top = new TopSongRow { Track = T("a"), Rank = 1 };

        Assert.False(row.IsTop);
        Assert.True(top.IsTop);
    }
}
