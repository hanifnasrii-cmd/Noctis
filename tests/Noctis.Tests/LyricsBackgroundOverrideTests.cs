using Avalonia.Headless.XUnit;
using Noctis.Helpers;
using Noctis.Models;
using Noctis.Services;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Per-song / per-album lyrics background videos (2026-09-07): a song's own clip beats its
/// album's, which beats the Settings default; a recorded clip whose file is gone is skipped;
/// and the "pause video with playback" option holds the clip only while playback is paused.
/// </summary>
public class LyricsBackgroundOverrideTests
{
    private static string TempClip()
    {
        var p = Path.Combine(Path.GetTempPath(), $"noctis-clip-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        return p;
    }

    private static PlayerViewModel MakePlayer()
        => new(new FakeAudioPlayer(), new FakeLibraryService(), new TestPersistenceService(), new FakeAnimatedCoverService());

    [Fact]
    public void Keys_AreStableAndDistinctPerKind()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var track = new Track { Id = id };
        var album = new Album { Id = id };
        Assert.Equal("track:11111111222233334444555555555555", LyricsBackgroundOverrides.KeyForTrack(track));
        Assert.Equal("album:11111111222233334444555555555555", LyricsBackgroundOverrides.KeyForAlbum(album));
        Assert.Equal(LyricsBackgroundOverrides.KeyForAlbum(album), LyricsBackgroundOverrides.KeyForAlbumId(id));
    }

    [AvaloniaFact]
    public void Resolve_SongBeatsAlbumBeatsDefault_AndSkipsMissingFiles()
    {
        var songClip = TempClip(); var albumClip = TempClip(); var defaultClip = TempClip();
        try
        {
            var albumId = Guid.NewGuid();
            var withOwn = new Track { Id = Guid.NewGuid(), AlbumId = albumId, Title = "own" };
            var albumOnly = new Track { Id = Guid.NewGuid(), AlbumId = albumId, Title = "album" };
            var plain = new Track { Id = Guid.NewGuid(), AlbumId = Guid.NewGuid(), Title = "plain" };
            var gone = new Track { Id = Guid.NewGuid(), AlbumId = Guid.NewGuid(), Title = "gone" };

            var player = MakePlayer();
            player.SetLyricsBackgroundSources(defaultClip, new Dictionary<string, string>
            {
                [LyricsBackgroundOverrides.KeyForTrack(withOwn)] = songClip,
                [LyricsBackgroundOverrides.KeyForAlbumId(albumId)] = albumClip,
                [LyricsBackgroundOverrides.KeyForTrack(gone)] = Path.Combine(Path.GetTempPath(), "does-not-exist.mp4"),
            });

            Assert.Equal(songClip, player.ResolveLyricsBackgroundFor(withOwn));
            Assert.Equal(albumClip, player.ResolveLyricsBackgroundFor(albumOnly));
            Assert.Equal(defaultClip, player.ResolveLyricsBackgroundFor(plain));
            Assert.Equal(defaultClip, player.ResolveLyricsBackgroundFor(gone));
            Assert.Equal(defaultClip, player.ResolveLyricsBackgroundFor(null));

            // The bound path follows the current track.
            player.CurrentTrack = withOwn;
            Assert.Equal(songClip, player.LyricsBackgroundMediaPath);
            player.CurrentTrack = albumOnly;
            Assert.Equal(albumClip, player.LyricsBackgroundMediaPath);
            player.CurrentTrack = plain;
            Assert.Equal(defaultClip, player.LyricsBackgroundMediaPath);
        }
        finally { File.Delete(songClip); File.Delete(albumClip); File.Delete(defaultClip); }
    }

    [AvaloniaFact]
    public void HoldsWhilePaused_OnlyWithTheOptionOn_AndOnlyWhileNotPlaying()
    {
        var player = MakePlayer();
        player.State = PlaybackState.Paused;
        Assert.False(player.LyricsBackgroundHoldsWhilePaused); // option off: keep looping

        player.LyricsBackgroundPausesWithPlayback = true;
        Assert.True(player.LyricsBackgroundHoldsWhilePaused);

        player.State = PlaybackState.Playing;
        Assert.False(player.LyricsBackgroundHoldsWhilePaused);
    }
}
