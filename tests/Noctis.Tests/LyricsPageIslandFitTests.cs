using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Models;
using Noctis.Services;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Discord report (Mistery, 2026-09-10, "corners cut off on a big TV"): on a short window
/// the lyrics page's info column is narrower than the compact island (340 + extras), and
/// the bar clips to its bounds, so the pill's rounded ends were chopped straight. The bar
/// must never be arranged narrower than its own pill.
/// </summary>
[Collection("MetadataServiceStatics")]
public class LyricsPageIslandFitTests
{
    private readonly ITestOutputHelper _output;
    public LyricsPageIslandFitTests(ITestOutputHelper output) => _output = output;

    private sealed class StubLrcLib : ILrcLibService
    {
        public Task<LrcLibResult?> GetLyricsAsync(string artist, string trackName, double durationSeconds, CancellationToken ct = default)
            => Task.FromResult<LrcLibResult?>(null);
        public Task<List<LrcLibResult>> SearchLyricsAsync(string artist, string trackName, CancellationToken ct = default)
            => Task.FromResult(new List<LrcLibResult>());
    }

    private sealed class StubNetEase : INetEaseService
    {
        public Task<LrcLibResult?> SearchLyricsAsync(string artist, string trackName, double durationSeconds, CancellationToken ct = default)
            => Task.FromResult<LrcLibResult?>(null);
    }

    private sealed class StubMetadata : IMetadataService
    {
        public Track? ReadTrackMetadata(string filePath) => null;
        public Track? ReadTrackMetadata(string filePath, out byte[]? embeddedArt) { embeddedArt = null; return null; }
        public byte[]? ExtractAlbumArt(string filePath) => null;
        public bool WriteTrackMetadata(Track track) => false;
        public bool WriteTrackMetadata(Track track, string targetFilePath, string? titleOverride = null) => false;
        public bool WriteAlbumArt(string filePath, byte[]? imageData) => false;
        public bool WriteRating(string filePath, int rating, bool isDisliked) => false;
        bool IMetadataService.WriteAdvancedFields(string filePath, AdvancedTagIO.AdvancedFields fields,
            AdvancedTagIO.AdvancedFields original) => false;
        public AudioFileInfo? ReadFileInfo(string filePath) => null;
    }

    private static (PlayerViewModel Player, LyricsViewModel Lyrics) MakeViewModels()
    {
        var player = new PlayerViewModel(
            new FakeAudioPlayer(), new FakeLibraryService(),
            new TestPersistenceService(), new FakeAnimatedCoverService());
        var lyrics = new LyricsViewModel(
            player, new StubLrcLib(), new StubNetEase(), new StubMetadata(),
            new TestPersistenceService(), new FakeLibraryService());
        return (player, lyrics);
    }

    private static void Pump(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(1000, 700, false)] // column 330 < island 340
    [InlineData(1000, 700, true)]  // column 330 < island 340 + Repeat extra
    [InlineData(1200, 650, true)]  // column 300 (the floor) < island 374
    public void CompactIsland_IsNeverClippedByAShortLyricsPage(double width, double height, bool repeatExtra)
    {
        var (player, vm) = MakeViewModels();
        player.IsLyricsPageActive = true;
        player.IslandShowRepeat = repeatExtra;
        var view = new LyricsView { DataContext = vm };
        var win = new Window { Width = width, Height = height, Content = view };
        try
        {
            win.Show();
            Pump(40);

            var bar = view.GetVisualDescendants().OfType<PlaybackBarView>().First();
            var island = bar.GetVisualDescendants().OfType<Border>().First(b => b.Name == "IslandBorder");
            var stack = view.FindControl<StackPanel>("LeftContentStack")!;
            var islandInBar = island.TransformToVisual(bar)!.Value.Transform(new Point(0, 0)).X;
            _output.WriteLine($"stack={stack.Bounds.Width:F1} bar={bar.Bounds.Width:F1} island={island.Bounds.Width:F1} islandX(in bar)={islandInBar:F1} clips={bar.ClipToBounds}");

            // Sanity: this window really is too short for the column to fit the pill.
            Assert.True(stack.Bounds.Width < island.Bounds.Width, "harness no longer reproduces a narrow column");

            // The bar clips to its bounds, so it must be at least as wide as its pill and
            // the pill must sit fully inside it — otherwise the rounded ends are cut.
            Assert.True(bar.Bounds.Width >= island.Bounds.Width - 0.5,
                $"bar {bar.Bounds.Width:F1} narrower than its island {island.Bounds.Width:F1}");
            Assert.True(islandInBar >= -0.5, $"island starts {islandInBar:F1} left of the bar's clip");
            Assert.True(islandInBar + island.Bounds.Width <= bar.Bounds.Width + 0.5, "island runs past the bar's clip");
        }
        finally
        {
            win.Close();
        }
    }
}
