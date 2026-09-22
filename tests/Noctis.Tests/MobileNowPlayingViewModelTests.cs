using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Noctis.Mobile.ViewModels;
using Noctis.Models;
using Noctis.Services;
using Xunit;

namespace Noctis.Tests;

/// <summary>Phone transport over IAudioPlayer + Core PlaybackQueue, with queue persistence.</summary>
public class MobileNowPlayingViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static Track[] Tracks(int n) => Enumerable.Range(0, n)
        .Select(i => new Track { Id = Guid.NewGuid(), Title = $"t{i}", FilePath = $"content://x/t{i}", Duration = TimeSpan.FromSeconds(100 + i) })
        .ToArray();

    private (NowPlayingViewModel Vm, FakeAudioPlayer Player, FakeLibraryService Library, PersistenceService Persistence) Make(Track[] tracks)
    {
        var player = new FakeAudioPlayer();
        var library = new FakeLibraryService();
        library.TrackList.AddRange(tracks);
        var persistence = new PersistenceService(_root);
        var vm = new NowPlayingViewModel(player, library, persistence, marshal: a => a());
        return (vm, player, library, persistence);
    }

    [Fact]
    public void PlayTracks_StartsTheChosenTrack_AndPreparesTheNextOneForGapless()
    {
        var t = Tracks(3);
        var (vm, player, _, _) = Make(t);

        vm.PlayTracks(t, 1);

        Assert.Same(t[1], vm.CurrentTrack);
        Assert.True(vm.IsPlaying);
        Assert.Equal(new[] { t[1].FilePath }, player.PlayedPaths);
        Assert.Equal(new[] { t[2].FilePath }, player.PreparedPaths);
        Assert.Equal(new[] { t[2] }, vm.UpNext);
        Assert.Equal(t[1].Duration, vm.Duration);
    }

    [Fact]
    public void TrackEnded_AdvancesNaturally_ThenStopsAtTheEndWithRepeatOff()
    {
        var t = Tracks(2);
        var (vm, player, _, _) = Make(t);
        vm.PlayTracks(t, 0);

        player.RaiseTrackEnded();
        Assert.Same(t[1], vm.CurrentTrack);
        Assert.Equal(new[] { t[0].FilePath, t[1].FilePath }, player.PlayedPaths);
        Assert.Empty(vm.UpNext);

        player.RaiseTrackEnded();
        Assert.False(vm.IsPlaying);
        Assert.Equal(PlaybackState.Stopped, player.State);
        Assert.Same(t[1], vm.CurrentTrack); // stays loaded for the mini bar
    }

    [Fact]
    public void Previous_RestartsAfterThreeSeconds_ElseGoesBack()
    {
        var t = Tracks(2);
        var (vm, player, _, _) = Make(t);
        vm.PlayTracks(t, 0);
        vm.NextCommand.Execute(null);
        Assert.Same(t[1], vm.CurrentTrack);

        vm.PreviousCommand.Execute(null);                  // position 0 → back
        Assert.Same(t[0], vm.CurrentTrack);
        Assert.Equal(3, player.PlayedPaths.Count);

        vm.Seek(TimeSpan.FromSeconds(10));
        vm.PreviousCommand.Execute(null);                  // >3 s → restart, no queue change
        Assert.Same(t[0], vm.CurrentTrack);
        Assert.Equal(TimeSpan.Zero, vm.Position);
        Assert.Equal(3, player.PlayedPaths.Count);
    }

    [Fact]
    public async Task SaveAndRestore_ComeBackPausedAtThePosition_AndResumeFromIt()
    {
        var t = Tracks(3);
        var (vm, player, library, persistence) = Make(t);
        vm.PlayTracks(t, 0);
        vm.CycleRepeatCommand.Execute(null);               // Off → All
        vm.Seek(TimeSpan.FromSeconds(42));
        await vm.SaveStateAsync();

        var restored = new NowPlayingViewModel(new FakeAudioPlayer(), library, persistence, marshal: a => a());
        await restored.RestoreStateAsync();

        Assert.Same(t[0], restored.CurrentTrack);
        Assert.False(restored.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(42), restored.Position);
        Assert.Equal(new[] { t[1], t[2] }, restored.UpNext);
        Assert.Equal(RepeatMode.All, restored.RepeatMode);
    }

    [Fact]
    public async Task TogglePlayPause_AfterRestore_PlaysFromTheSavedPosition()
    {
        var t = Tracks(1);
        var (vm, _, library, persistence) = Make(t);
        vm.PlayTracks(t, 0);
        vm.Seek(TimeSpan.FromSeconds(30));
        await vm.SaveStateAsync();

        var player = new FakeAudioPlayer();
        var restored = new NowPlayingViewModel(player, library, persistence, marshal: a => a());
        await restored.RestoreStateAsync();
        restored.TogglePlayPauseCommand.Execute(null);

        Assert.Equal(30_000, player.PendingSeekMs);
        Assert.Equal(new[] { t[0].FilePath }, player.PlayedPaths);
        Assert.True(restored.IsPlaying);
    }

    [Fact]
    public void QueueEdits_ReprepareTheGaplessNext()
    {
        var t = Tracks(4);
        var (vm, player, _, _) = Make(t);
        vm.PlayTracks(t.Take(2).ToArray(), 0);           // upnext [t1], prepared t1
        vm.PlayNext(t[2]);                                 // upnext [t2, t1]
        Assert.Equal(t[2].FilePath, player.PreparedPaths.Last());
        vm.RemoveFromQueue(0);                             // upnext [t1]
        Assert.Equal(t[1].FilePath, player.PreparedPaths.Last());
        vm.AddToQueue(t[3]);
        Assert.Equal(new[] { t[1], t[3] }, vm.UpNext);
    }

    [Fact]
    public void FiveConsecutiveErrors_StopInsteadOfLooping()
    {
        var t = Tracks(10);
        var (vm, player, _, _) = Make(t);
        vm.PlayTracks(t, 0);
        for (int i = 0; i < 5; i++) player.RaisePlaybackError("boom");

        Assert.False(vm.IsPlaying);
        Assert.Equal(5, player.PlayedPaths.Count);         // t0 + four skips, then stop
        Assert.NotEqual(string.Empty, vm.ErrorText);
    }

    [Fact]
    public async Task SaveAndRestore_ShuffleOn_TogglingOffAfterwardsRestoresAlbumOrder()
    {
        var t = Tracks(4);
        var (vm, _, library, persistence) = Make(t);
        vm.PlayTracks(t, 0);
        vm.ToggleShuffleCommand.Execute(null);             // scrambles UpNext, remembers album order
        await vm.SaveStateAsync();

        var restored = new NowPlayingViewModel(new FakeAudioPlayer(), library, persistence, marshal: a => a());
        await restored.RestoreStateAsync();
        Assert.True(restored.IsShuffleEnabled);

        restored.ToggleShuffleCommand.Execute(null);       // off → restores the pre-shuffle order

        Assert.False(restored.IsShuffleEnabled);
        Assert.Equal(new[] { t[1], t[2], t[3] }, restored.UpNext);
    }

    [Fact]
    public void SetGapless_False_CancelsInsteadOfPreparing()
    {
        var t = Tracks(3);
        var (vm, player, _, _) = Make(t);
        vm.SetGapless(false);

        vm.PlayTracks(t, 0);

        Assert.False(player.GaplessEnabled);
        Assert.Empty(player.PreparedPaths);
        Assert.True(player.CancelledCount > 0);
    }

    [Fact]
    public void RepeatOne_PlayTracks_CancelsPreparedNextInsteadOfPreparingIt()
    {
        var t = Tracks(3);
        var (vm, player, _, _) = Make(t);
        vm.CycleRepeatCommand.Execute(null);               // Off → All
        vm.CycleRepeatCommand.Execute(null);               // All → One
        Assert.Equal(RepeatMode.One, vm.RepeatMode);
        // Each CycleRepeat above already ran PrepareUpcoming with no current track, which
        // calls CancelPreparedNext regardless of repeat mode — snapshot after those so the
        // assertion below is actually load-bearing on PlayTracks's own PrepareUpcoming call.
        var cancelledBeforePlay = player.CancelledCount;

        vm.PlayTracks(t, 0);

        Assert.Empty(player.PreparedPaths);
        Assert.True(player.CancelledCount > cancelledBeforePlay);
    }

    [Fact]
    public void FocusLoss_PlayerPausedExternally_SyncsIsPlayingOnTheNextPositionTick()
    {
        var t = Tracks(1);
        var (vm, player, _, _) = Make(t);
        vm.PlayTracks(t, 0);
        Assert.True(vm.IsPlaying);

        player.Pause();                                    // audio-focus loss / lock-screen pause, bypassing the VM
        player.RaisePositionChanged(TimeSpan.FromSeconds(1));

        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public void SeekDrag_SuspendsThePositionPush_UntilTheDragEnds()
    {
        // The drag itself cannot be simulated headlessly, so this pins the mechanism the
        // pointer handlers drive: while a seek is in progress the 4 Hz position tick must not
        // move Position, because that value is bound into the Slider and would overwrite what
        // the user's finger put there. Everything else the tick does must keep working.
        var t = Tracks(1);
        var (vm, player, _, _) = Make(t);
        vm.PlayTracks(t, 0);

        vm.BeginSeek();
        player.RaisePositionChanged(TimeSpan.FromSeconds(12));
        Assert.Equal(TimeSpan.Zero, vm.Position);

        // ...while the rest of the tick still runs: an external pause mid-drag is still caught.
        player.Pause();
        player.RaisePositionChanged(TimeSpan.FromSeconds(13));
        Assert.False(vm.IsPlaying);

        vm.EndSeek();
        player.Resume();
        player.RaisePositionChanged(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(20), vm.Position);
    }

    [Fact]
    public void QueueAvailability_DrivesTheNotificationTransportButtons()
    {
        // These two feed Media3's HasNext/HasPreviousMediaItem, which decide whether the
        // notification draws Next/Previous enabled. ExoPlayer's own item list is the wrong
        // source, so they are computed from the app queue instead — pinned here.
        var t = Tracks(2);
        var (vm, _, _, _) = Make(t);

        Assert.False(vm.HasNext);                          // stopped, empty queue
        Assert.False(vm.HasPrevious);

        vm.PlayTracks(t, 0);
        Assert.True(vm.HasNext);                           // t[1] is up next
        Assert.True(vm.HasPrevious);                       // Previous restarts the track

        vm.NextCommand.Execute(null);
        Assert.Empty(vm.UpNext);
        Assert.False(vm.HasNext);                          // last track, repeat off
        Assert.True(vm.HasPrevious);                       // history has t[0]

        vm.CycleRepeatCommand.Execute(null);               // Off -> All
        Assert.Equal(RepeatMode.All, vm.RepeatMode);
        // The widening this task added: Repeat All wraps to the recorded cycle even with
        // UpNext empty, so the notification's Next must stay enabled on the last track.
        Assert.True(vm.HasNext);

        vm.CycleRepeatCommand.Execute(null);               // All -> One
        Assert.Equal(RepeatMode.One, vm.RepeatMode);
        Assert.False(vm.HasNext);                          // One does not wrap on a user skip
    }
}
