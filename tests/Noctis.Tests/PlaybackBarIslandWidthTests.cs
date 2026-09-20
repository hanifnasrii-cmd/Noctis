using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The player-bar island carries a 200ms Width transition, and the lyrics page mounts its
/// own copy of the bar in the compact (340px) state while the XAML declares the base width
/// (626px). Avalonia leaves transitions enabled on a control that has never been detached,
/// so the mount-time write used to play as a visible full→340 shrink the first time the
/// lyrics page opened. Establishing writes must land instantly; only live state changes
/// may animate.
/// </summary>
public class PlaybackBarIslandWidthTests
{
    private const double IslandBaseWidth = 536; // 626 with the long track info, 590 before the favorite heart
    private const double IslandLyricsPageWidth = 340;

    private static PlayerViewModel MakePlayer() => new(
        new FakeAudioPlayer(), new FakeLibraryService(),
        new TestPersistenceService(), new FakeAnimatedCoverService());

    private static Border Island(PlaybackBarView bar) =>
        Assert.IsType<Border>(bar.FindControl<Border>("IslandBorder"));

    [AvaloniaFact]
    public void MountingOnTheLyricsPage_StartsCompactWithoutAnimatingDown()
    {
        var player = MakePlayer();
        player.IsLyricsPageActive = true;

        var bar = new PlaybackBarView { DataContext = player };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            // Read before any render tick: a running transition would still be at (or
            // near) the XAML base width here and glide down over the next 200ms.
            Assert.Equal(IslandLyricsPageWidth, Island(bar).Width);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void MountingOnTheMainWindow_StaysAtTheBaseWidth()
    {
        var player = MakePlayer();
        player.IsLyricsPageActive = true;

        // The persistent bottom bar opts out of the compact state entirely.
        var bar = new PlaybackBarView { DataContext = player, CompactWhenLyricsPageActive = false };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(IslandBaseWidth, Island(bar).Width);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void DataContextArrivingAfterMount_StillResolvesTheCompactWidth()
    {
        var player = MakePlayer();
        player.IsLyricsPageActive = true;

        var bar = new PlaybackBarView();
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IslandBaseWidth, Island(bar).Width);

            bar.DataContext = player;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(IslandLyricsPageWidth, Island(bar).Width);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void StoredUserResize_IsEstablishedInstantlyOnTheMainWindowBar()
    {
        var player = MakePlayer();
        // Simulates SettingsViewModel hydrating a persisted resize at startup.
        player.PlaybackBarIslandWidth = 720;

        var bar = new PlaybackBarView { DataContext = player, CompactWhenLyricsPageActive = false };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            // Same establishing-write rule as the compact mount: the stored width must
            // be in place before any render tick, never animated into.
            Assert.Equal(720, Island(bar).Width);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void NarrowUserResize_HidesTrackInfo_AndWideningRestoresIt()
    {
        var player = MakePlayer();
        player.PlaybackBarIslandWidth = 420; // below the 500px compact-shape threshold

        var bar = new PlaybackBarView { DataContext = player, CompactWhenLyricsPageActive = false };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var trackInfo = bar.FindControl<Grid>("TrackInfoPanel");
            Assert.NotNull(trackInfo);
            Assert.Equal(420, Island(bar).Width);
            Assert.False(trackInfo!.IsVisible);

            // Back to the stock width: the full layout returns.
            player.PlaybackBarIslandWidth = IslandBaseWidth;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(IslandBaseWidth, Island(bar).Width);
            Assert.True(trackInfo.IsVisible);
        }
        finally
        {
            win.Close();
        }
    }

    /// <summary>The "…" lives inside the track box; the right cluster's OptionsButton
    /// (which owns the MenuFlyout) only stands in while the box is hidden — the compact
    /// resize shape and the lyrics page — so the lyrics-only menu entries stay reachable.</summary>
    [AvaloniaFact]
    public void OptionsFallback_ShowsOnlyWhileTheTrackBoxIsHidden()
    {
        var player = MakePlayer();

        var bar = new PlaybackBarView { DataContext = player, CompactWhenLyricsPageActive = false };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var trackInfo = bar.FindControl<Grid>("TrackInfoPanel")!;
            var boxDots = bar.FindControl<Button>("BoxOptionsButton")!;
            var fallbackDots = bar.FindControl<Button>("OptionsButton")!;

            Assert.True(trackInfo.IsVisible);
            Assert.True(boxDots.IsVisible);
            Assert.False(fallbackDots.IsVisible);

            player.PlaybackBarIslandWidth = 420; // compact shape: box gone, fallback in
            Dispatcher.UIThread.RunJobs();

            Assert.False(trackInfo.IsVisible);
            Assert.True(fallbackDots.IsVisible);

            player.PlaybackBarIslandWidth = IslandBaseWidth;
            Dispatcher.UIThread.RunJobs();

            Assert.True(trackInfo.IsVisible);
            Assert.False(fallbackDots.IsVisible);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void RepeatAndFavorite_AreHiddenUntilTheirSettingsTurnThemOn()
    {
        var player = MakePlayer();

        var bar = new PlaybackBarView { DataContext = player, CompactWhenLyricsPageActive = false };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var heart = bar.FindControl<Button>("FavoriteButton")!;
            Assert.False(heart.IsVisible);
            Assert.Equal(IslandBaseWidth, Island(bar).Width);

            // Each extra widens the stock pill by one 34px button + 2px spacing,
            // exactly like shuffle / sleep / speed already do.
            player.IslandShowFavorite = true;
            player.IslandShowRepeat = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(heart.IsVisible);
            Assert.Equal(IslandBaseWidth + 2 * 36, Island(bar).Width);
        }
        finally
        {
            win.Close();
        }
    }
}
