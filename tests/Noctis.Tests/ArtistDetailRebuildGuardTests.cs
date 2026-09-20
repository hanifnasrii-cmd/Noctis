using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Artist page (09-14): hovering a song row "glitched" and a track menu re-opened right
/// after using one of its options would not open. Every LibraryUpdated (each play-count
/// save) and FavoritesChanged (each heart) ran an unconditional Clear+Add over every
/// list, so the hovered row and the menu's owner row were torn down each time. Lists are
/// now replaced only when their content changed.
/// </summary>
public class ArtistDetailRebuildGuardTests
{
    private static Track T(string title, string artist, Album album, int plays) => new()
    {
        Id = Guid.NewGuid(), Title = title, Artist = artist, AlbumArtist = artist, Album = album.Name, AlbumId = album.Id,
        PlayCount = plays, Duration = TimeSpan.FromSeconds(200), FilePath = "C:/m/" + title + ".mp3",
    };

    private static (ArtistDetailViewModel Vm, FakeLibraryService Lib, List<Track> Tracks) Make()
    {
        var lib = new FakeLibraryService();
        var album = new Album { Id = Guid.NewGuid(), Name = "Phases", Artist = "Chase Atlantic", Year = 2019 };
        var a = T("Angels", "Chase Atlantic", album, 9);
        var b = T("Her", "Chase Atlantic", album, 5);
        var c = T("Intro", "Chase Atlantic", album, 2);
        album.Tracks = new List<Track> { a, b, c };
        lib.TrackList.AddRange(album.Tracks);
        ((List<Album>)lib.Albums).Add(album);
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, new TestPersistenceService(), new FakeAnimatedCoverService());
        return (new ArtistDetailViewModel("Chase Atlantic", lib, player), lib, album.Tracks);
    }

    private static int CountResets(INotifyCollectionChanged col, Action act)
    {
        var n = 0;
        void H(object? s, NotifyCollectionChangedEventArgs e) => n++;
        col.CollectionChanged += H;
        act();
        Dispatcher.UIThread.RunJobs();
        col.CollectionChanged -= H;
        return n;
    }

    [AvaloniaFact]
    public void UnchangedLibraryUpdate_LeavesEveryListAlone()
    {
        var (vm, lib, _) = Make();
        Assert.Equal(3, vm.PopularSongs.Count);
        Assert.Single(vm.Releases);

        var popular = CountResets(vm.PopularSongs, lib.RaiseLibraryUpdated);
        var releases = CountResets(vm.Releases, lib.RaiseLibraryUpdated);
        var overview = CountResets(vm.OverviewAlbums, lib.RaiseLibraryUpdated);
        var favs = CountResets(vm.FavoriteSongs, lib.NotifyFavoritesChanged);

        Assert.Equal(0, popular);
        Assert.Equal(0, releases);
        Assert.Equal(0, overview);
        Assert.Equal(0, favs);
    }

    [AvaloniaFact]
    public void ARealChange_StillRebuildsTheAffectedList()
    {
        var (vm, lib, tracks) = Make();
        Assert.Empty(vm.FavoriteSongs);

        tracks[1].IsFavorite = true;
        var favs = CountResets(vm.FavoriteSongs, lib.NotifyFavoritesChanged);
        Assert.True(favs > 0);
        Assert.Equal("Her", vm.FavoriteSongs.Single().Track.Title);

        // Popular order unchanged by a heart → untouched even though favourites rebuilt.
        var popular = CountResets(vm.PopularSongs, lib.NotifyFavoritesChanged);
        Assert.Equal(0, popular);

        // A play-count change that reorders Top Songs rebuilds them.
        tracks[2].PlayCount = 50;
        var reordered = CountResets(vm.PopularSongs, lib.RaiseLibraryUpdated);
        Assert.True(reordered > 0);
        Assert.Equal("Intro", vm.PopularSongs.First().Track.Title);
    }

    [Fact]
    public void SameRows_ComparesTrackAndRank()
    {
        var album = new Album { Id = Guid.NewGuid(), Name = "x", Artist = "a" };
        var a = T("a", "a", album, 1); var b = T("b", "a", album, 1);
        var cur = new List<TopSongRow> { new() { Track = a, Rank = 1 }, new() { Track = b, Rank = 2 } };
        Assert.True(ArtistDetailViewModel.SameRows(cur, new List<TopSongRow> { new() { Track = a, Rank = 1 }, new() { Track = b, Rank = 2 } }));
        Assert.False(ArtistDetailViewModel.SameRows(cur, new List<TopSongRow> { new() { Track = b, Rank = 1 }, new() { Track = a, Rank = 2 } }));
        Assert.False(ArtistDetailViewModel.SameRows(cur, new List<TopSongRow> { new() { Track = a, Rank = 1 } }));
    }
}
