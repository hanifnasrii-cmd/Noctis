using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Noctis.Models;
using Noctis.Services;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Home's Last Played and Albums rows came back with only the current track after a
/// restart: they were built from Player.History, a transport list that StopAndClear
/// wipes when a queue plays out, and the shutdown snapshot then persisted an empty
/// history (the user's queue.json held 0 ids while play_history.json held 4082 events).
/// The rows now read the persisted play log, newest first.
/// </summary>
public class HomeRecentRowsFromPlayLogTests
{
    private sealed class FakePlayHistory : IPlayHistoryService
    {
        public List<PlayHistoryEvent> Log { get; } = new();
        public IReadOnlyList<PlayHistoryEvent> Events => Log;
        public Task PreloadAsync() => Task.CompletedTask;
        public void RecordPlay(Track track) => Log.Add(new PlayHistoryEvent { TrackId = track.Id, Title = track.Title, PlayedAtUtc = DateTime.UtcNow });
        public void RecordSkip(Track track) { }
        public Task FlushAsync() => Task.CompletedTask;
    }

    private static Track T(string title, Guid albumId, string album) => new()
    { Id = Guid.NewGuid(), Title = title, Artist = "A", AlbumArtist = "A", Album = album, AlbumId = albumId, Duration = TimeSpan.FromSeconds(180), FilePath = "C:/m/" + title + ".mp3" };

    [AvaloniaFact]
    public async Task Rows_ComeFromThePlayLog_NewestFirst_WhenPlayerHistoryIsEmpty()
    {
        var lib = new FakeLibraryService();
        var alb1 = Guid.NewGuid(); var alb2 = Guid.NewGuid();
        var a = T("Cruel Summer", alb1, "Lover");
        var b = T("Un Ratito", alb2, "Un Verano Sin Ti");
        var c = T("Lover", alb1, "Lover");
        lib.TrackList.AddRange(new[] { a, b, c });
        ((List<Album>)lib.Albums).Add(new Album { Id = alb1, Name = "Lover", Artist = "A", Tracks = new List<Track> { a, c } });
        ((List<Album>)lib.Albums).Add(new Album { Id = alb2, Name = "Un Verano Sin Ti", Artist = "A", Tracks = new List<Track> { b } });
        var persistence = new TestPersistenceService();
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, persistence, new FakeAnimatedCoverService());
        var log = new FakePlayHistory();
        // Yesterday's session, oldest first; one deleted track; a repeat of `a` at the end.
        log.RecordPlay(c); log.RecordPlay(a); log.RecordPlay(b);
        log.Log.Add(new PlayHistoryEvent { TrackId = Guid.NewGuid(), Title = "deleted", PlayedAtUtc = DateTime.UtcNow });
        log.RecordPlay(a);
        Assert.Empty(player.History); // what a fresh launch after a played-out queue looks like

        var vm = new HomeViewModel(player, lib, new SidebarViewModel(persistence, lib), playHistory: log);
        await vm.RefreshAsync();

        Assert.Equal(new[] { "Cruel Summer", "Un Ratito", "Lover" }, vm.LastPlayed.Select(t => t.Title));
        Assert.Equal(new[] { "Lover", "Un Verano Sin Ti" }, vm.RecentlyPlayedAlbums.Select(x => x.Name));
    }

    [Fact]
    public void BuildRecentFromLog_ScansOnlyTheNewestWindow_AndDropsUnresolved()
    {
        var known = new Dictionary<Guid, Track>();
        var events = new List<PlayHistoryEvent>();
        for (var i = 0; i < 10; i++)
        {
            var t = T("t" + i, Guid.NewGuid(), "x");
            known[t.Id] = t;
            events.Add(new PlayHistoryEvent { TrackId = t.Id });
        }
        events.Insert(5, new PlayHistoryEvent { TrackId = Guid.NewGuid() }); // unresolved

        var recent = HomeViewModel.BuildRecentFromLog(events, id => known.GetValueOrDefault(id), scan: 4);

        Assert.Equal(new[] { "t9", "t8", "t7", "t6" }, recent.Select(t => t.Title));
        var all = HomeViewModel.BuildRecentFromLog(events, id => known.GetValueOrDefault(id), scan: 100);
        Assert.Equal(10, all.Count);
        Assert.Equal("t0", all.Last().Title);
    }

    [AvaloniaFact]
    public async Task Rows_FallBackToPlayerHistory_WithoutAPlayLog()
    {
        var lib = new FakeLibraryService();
        var alb = Guid.NewGuid();
        var a = T("Style", alb, "1989"); var b = T("Blank Space", alb, "1989");
        lib.TrackList.AddRange(new[] { a, b });
        ((List<Album>)lib.Albums).Add(new Album { Id = alb, Name = "1989", Artist = "A", Tracks = new List<Track> { a, b } });
        var persistence = new TestPersistenceService();
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, persistence, new FakeAnimatedCoverService());
        player.History.Add(b); player.History.Add(a);

        var vm = new HomeViewModel(player, lib, new SidebarViewModel(persistence, lib));
        await vm.RefreshAsync();

        Assert.Equal(new[] { "Blank Space", "Style" }, vm.LastPlayed.Select(t => t.Title));
        Assert.Single(vm.RecentlyPlayedAlbums);
    }
}
