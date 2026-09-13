using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The artist page's data shape: releases are albums the artist is credited on,
/// Appears On is everything else they feature on, Popular ranks by play count,
/// the chips split albums from singles/EPs, and search narrows every section.
/// </summary>
public class ArtistDetailViewModelTests
{
    private static Album MakeAlbum(string name, string artist, int year, params (string title, string trackArtist, int plays)[] tracks)
    {
        var id = Guid.NewGuid();
        var album = new Album { Id = id, Name = name, Artist = artist, Year = year, Tracks = new List<Track>() };
        var n = 1;
        foreach (var (title, trackArtist, plays) in tracks)
        {
            album.Tracks.Add(new Track
            {
                Id = Guid.NewGuid(), Title = title, Artist = trackArtist, AlbumArtist = artist, Album = name,
                AlbumId = id, TrackNumber = n++, DiscNumber = 1, Year = year, Duration = TimeSpan.FromMinutes(3),
                PlayCount = plays,
            });
        }
        album.TrackCount = album.Tracks.Count;
        return album;
    }

    private static (ArtistDetailViewModel Vm, FakeLibraryService Lib) Make(string artist, params Album[] albums)
    {
        var lib = new FakeLibraryService();
        ((List<Album>)lib.Albums).AddRange(albums);
        foreach (var a in albums) lib.TrackList.AddRange(a.Tracks);
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, new TestPersistenceService(), new FakeAnimatedCoverService());
        return (new ArtistDetailViewModel(artist, lib, player), lib);
    }

    [Fact]
    public void Releases_AreCreditedAlbums_AppearsOn_IsFeatureOnly()
    {
        var own = MakeAlbum("Phases", "Chase Atlantic", 2019, ("Angels", "Chase Atlantic", 5), ("Her", "Chase Atlantic", 2));
        var collab = MakeAlbum("Duo", "Chase Atlantic & Friend", 2021, ("Together", "Chase Atlantic & Friend", 1));
        var feature = MakeAlbum("Other", "Someone Else", 2020,
            ("Solo", "Someone Else", 9), ("Feat", "Someone Else feat. Chase Atlantic", 4));
        var unrelated = MakeAlbum("Nope", "Nobody", 2018, ("X", "Nobody", 0));

        var (vm, _) = Make("Chase Atlantic", own, collab, feature, unrelated);

        Assert.Equal(new[] { "Duo", "Phases" }, vm.Releases.Select(a => a.Name)); // newest first
        Assert.Equal(new[] { "Other" }, vm.AppearsOn.Select(a => a.Name));
        Assert.Same(collab, vm.LatestRelease);
        Assert.Equal(2, vm.ReleaseCount);
        Assert.Equal(4, vm.SongCount);
    }

    [Fact]
    public void Popular_RanksByPlayCount_ThenTitle_AndIncludesFeatures()
    {
        var own = MakeAlbum("Phases", "Chase Atlantic", 2019,
            ("Angels", "Chase Atlantic", 5), ("Her", "Chase Atlantic", 2), ("Zzz", "Chase Atlantic", 2));
        var feature = MakeAlbum("Other", "Someone Else", 2020, ("Feat", "Someone Else feat. Chase Atlantic", 7));

        var (vm, _) = Make("Chase Atlantic", own, feature);

        Assert.Equal(new[] { "Feat", "Angels", "Her", "Zzz" }, vm.PopularSongs.Select(r => r.Track.Title));
        Assert.Equal(new[] { 1, 2, 3, 4 }, vm.PopularSongs.Select(r => r.Rank));
        Assert.True(vm.PopularSongs[0].IsTop);
    }

    [Fact]
    public void Popular_IsCapped()
    {
        var tracks = Enumerable.Range(0, 25).Select(i => ($"T{i:00}", "A", 25 - i)).ToArray();
        var (vm, _) = Make("A", MakeAlbum("Big", "A", 2000, tracks));
        Assert.Equal(ArtistDetailViewModel.MaxPopular, vm.PopularSongs.Count);
    }

    [Fact]
    public void Tabs_SplitAlbumsFromSinglesAndEps()
    {
        var album = MakeAlbum("LP", "A", 2020, Enumerable.Range(0, 8).Select(i => ($"t{i}", "A", 0)).ToArray());
        var single = MakeAlbum("Single", "A", 2021, ("s", "A", 0));
        var ep = MakeAlbum("EP", "A", 2022, ("e1", "A", 0), ("e2", "A", 0), ("e3", "A", 0));
        var (vm, _) = Make("A", album, single, ep);

        Assert.Equal(3, vm.Releases.Count);
        Assert.True(vm.IsTabOverview);
        Assert.Equal(new[] { "LP" }, vm.AlbumReleases.Select(a => a.Name));
        Assert.Equal(new[] { "EP", "Single" }, vm.SingleReleases.Select(a => a.Name));
        Assert.Equal(1, vm.AlbumCount);
        Assert.Equal(2, vm.SingleCount);

        vm.SelectTabCommand.Execute("albums");
        Assert.True(vm.IsTabAlbums);
        vm.SelectTabCommand.Execute("singles");
        Assert.True(vm.IsTabSingles);
        vm.SelectTabCommand.Execute("similar");
        Assert.True(vm.IsTabSimilar);
        Assert.True(vm.SimilarLoaded);                    // no service wired → settles empty
        Assert.True(vm.ShowNoSimilar);
        vm.SelectTabCommand.Execute("garbage");
        Assert.True(vm.IsTabOverview);
    }

    [Fact]
    public void OverviewRows_NewestFourEach_ALoneRowTakesEight_SearchLiftsTheCap()
    {
        var albums = Enumerable.Range(0, 10).Select(i =>
            MakeAlbum($"LP {i:00}", "A", 2000 + i, Enumerable.Range(0, 8).Select(k => ($"lp{i}t{k}", "A", 0)).ToArray()));
        var singles = Enumerable.Range(0, 6).Select(i => MakeAlbum($"Single {i:00}", "A", 2010 + i, ($"s{i}", "A", 0)));
        var (vm, _) = Make("A", albums.Concat(singles).ToArray());

        Assert.Equal(ArtistDetailViewModel.OverviewRowTiles, vm.OverviewAlbums.Count);
        Assert.Equal(ArtistDetailViewModel.OverviewRowTiles, vm.OverviewSingles.Count);
        Assert.Equal("LP 09", vm.OverviewAlbums[0].Name);          // newest first
        Assert.Equal(1, vm.AlbumsSpan);
        Assert.Equal(2, vm.SinglesColumn);
        Assert.Equal(ArtistDetailViewModel.OverviewRowTiles, vm.OverviewAlbumColumns);

        vm.ApplyFilter("lp");                                       // every album matches; no cap
        Assert.Equal(10, vm.OverviewAlbums.Count);
        Assert.Empty(vm.OverviewSingles);
        Assert.Equal(3, vm.AlbumsSpan);                             // the row spans both columns …
        Assert.Equal(ArtistDetailViewModel.GridColumns, vm.OverviewAlbumColumns); // … eight across

        vm.ApplyFilter("");
        var (only, _) = Make("B", Enumerable.Range(0, 10).Select(i =>
            MakeAlbum($"B {i:00}", "B", 2000 + i, Enumerable.Range(0, 8).Select(k => ($"b{i}t{k}", "B", 0)).ToArray())).ToArray());
        Assert.Equal(ArtistDetailViewModel.GridColumns, only.OverviewAlbums.Count); // alone → eight newest
        Assert.False(only.HasOverviewSingles);
    }

    [Fact]
    public void LatestReleaseLines_KindYearAndSongsLength()
    {
        var lp = MakeAlbum("LP", "A", 2024, Enumerable.Range(0, 8).Select(i => ($"t{i}", "A", 0)).ToArray());
        Assert.Equal("Album · 2024", ArtistDetailViewModel.LatestReleaseKindLineFor(lp));
        Assert.Equal("8 songs · 24:00", ArtistDetailViewModel.LatestReleaseSongsLineFor(lp));
        var ep = MakeAlbum("EP", "A", 0, ("e1", "A", 0), ("e2", "A", 0), ("e3", "A", 0));
        Assert.Equal("EP", ArtistDetailViewModel.LatestReleaseKindLineFor(ep));
    }

    [Fact]
    public void Search_NarrowsReleasesAppearsOnAndPopular()
    {
        var own = MakeAlbum("Phases", "A", 2019, ("Angels", "A", 5), ("Her", "A", 2));
        var other = MakeAlbum("Beauty", "A", 2021, ("Cassie", "A", 1));
        var feature = MakeAlbum("Guest Spot", "B", 2020, ("Angels Remix", "B feat. A", 4));
        var (vm, _) = Make("A", own, other, feature);

        vm.ApplyFilter("angel");
        // Angels (5 plays) outranks Angels Remix (4) — search narrows, ranking stays.
        Assert.Equal(new[] { "Angels", "Angels Remix" }, vm.PopularSongs.Select(r => r.Track.Title));
        // Albums answer through their track titles too: Phases carries "Angels",
        // Guest Spot carries "Angels Remix"; Beauty has neither.
        Assert.Equal(new[] { "Phases" }, vm.Releases.Select(a => a.Name));
        Assert.Equal(new[] { "Guest Spot" }, vm.AppearsOn.Select(a => a.Name));

        vm.ApplyFilter("");
        Assert.Equal(2, vm.Releases.Count);
        Assert.Single(vm.AppearsOn);
    }

    [Fact]
    public void AllTracks_ReleasesInAlbumOrder_ThenFeatures_NoDuplicates()
    {
        var newer = MakeAlbum("New", "A", 2021, ("n2", "A", 0), ("n1", "A", 0));
        var older = MakeAlbum("Old", "A", 2019, ("o1", "A", 0));
        var feature = MakeAlbum("Guest", "B", 2020, ("solo", "B", 0), ("with A", "B & A", 0));
        var (vm, _) = Make("A", newer, older, feature);

        var all = vm.GetAllTracks();
        Assert.Equal(new[] { "n2", "n1", "o1", "with A" }, all.Select(t => t.Title));
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void LibraryUpdate_UnsubscribesOnDispose()
    {
        var (vm, lib) = Make("A", MakeAlbum("X", "A", 2020, ("t", "A", 0)));
        vm.Dispose();
        lib.RaiseLibraryUpdated(); // must not throw or touch the disposed page
    }

    [Fact]
    public void UnknownArtist_GetsASynthesizedIdentity()
    {
        var (vm, _) = Make("Nobody Here");
        Assert.Equal("Nobody Here", vm.Artist.Name);
        Assert.NotEqual(Guid.Empty, vm.Artist.Id);
        Assert.Empty(vm.Releases);
        Assert.False(vm.HasPopular);
    }
}
