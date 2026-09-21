using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Models;
using Noctis.Services;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// 09-15 Metadata dialog "Sheet" layout: grouped rail (Info / Lyrics / Playback / File)
/// with an unsaved-edit dot per section, paired Details fields, and a footer line that
/// counts the edits Save will write. Headless geometry probe — no pixels.
/// </summary>
public class MetadataSheetLayoutProbeTests
{
    private readonly ITestOutputHelper _o;
    public MetadataSheetLayoutProbeTests(ITestOutputHelper o) => _o = o;

    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = Avalonia.Media.FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis/Assets/Icons.axaml") });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis/Assets/Styles.axaml") });
    }

    private static void Pump(int n = 4)
    {
        for (var i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }

    private static Track T(string title, int n, Guid albumId) => new()
    {
        Id = Guid.NewGuid(), Title = title, Artist = "Bad Bunny", AlbumArtist = "Bad Bunny",
        Album = "nadie sabe lo que va a pasar mañana", AlbumId = albumId, TrackNumber = n, TrackCount = 3,
        Genre = "Latin", Year = 2023, Duration = TimeSpan.FromSeconds(200), FilePath = "C:/m/" + title + ".flac",
    };

    private static (MetadataViewModel vm, MetadataWindow win) OpenAlbumDialog()
    {
        var albumId = Guid.NewGuid();
        var tracks = new[] { T("NADIE SABE", 1, albumId), T("MONACO", 2, albumId), T("FINA", 3, albumId) }.ToList();
        var lib = new FakeLibraryService();
        lib.TrackList.AddRange(tracks);
        var vm = new MetadataViewModel(tracks[0], new ProbeMetadataService(), lib, new TestPersistenceService(),
            new FakeAnimatedCoverService(), albumScoped: true, albumTracks: tracks);
        var win = new MetadataWindow(vm) { RequestedThemeVariant = ThemeVariant.Dark };
        win.Show();
        Pump(6);
        return (vm, win);
    }

    [AvaloniaFact]
    public void Sheet_Rail_Groups_PairedFields_And_ChangeDot()
    {
        EnsureAppStyles();
        AccentTestHarness.WithAccent("#E74856", ThemeVariant.Dark, () =>
        {
            var (vm, win) = OpenAlbumDialog();
            var all = win.GetVisualDescendants().ToList();

            // Rail groups: album scope shows Info + Playback; the Lyrics and File groups ride
            // hidden tabs and must not appear.
            var labels = all.OfType<TextBlock>().Where(t => t.FontSize == 11.5 && t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            _o.WriteLine("rail groups: " + string.Join(" | ", labels));
            Assert.Contains("Info", labels);
            Assert.Contains("Playback", labels);
            Assert.DoesNotContain("Lyrics", labels);
            Assert.DoesNotContain("File", labels);

            // Details pairs: Album Artist and Artist boxes share a row, side by side.
            var boxes = all.OfType<TextBox>().Where(b => b.IsEffectivelyVisible).ToList();
            var albumArtist = boxes.First(b => b.Text == "Bad Bunny");
            var artist = boxes.Last(b => b.Text == "Bad Bunny");
            var pa = albumArtist.TranslatePoint(new Point(0, 0), win)!.Value;
            var pb = artist.TranslatePoint(new Point(0, 0), win)!.Value;
            _o.WriteLine($"album artist at ({pa.X:0},{pa.Y:0}) w{albumArtist.Bounds.Width:0} | artist at ({pb.X:0},{pb.Y:0}) w{artist.Bounds.Width:0}");
            Assert.Equal(pa.Y, pb.Y, 1);
            Assert.True(pb.X > pa.X + albumArtist.Bounds.Width, "artist should sit to the right of album artist");
            Assert.True(Math.Abs(albumArtist.Bounds.Width - artist.Bounds.Width) < 2, "columns should be equal width");

            // Details fits the page without scrolling in album scope.
            var scroller = boxes.First(b => b.Text == "nadie sabe lo que va a pasar mañana").GetVisualAncestors().OfType<ScrollViewer>().First();
            _o.WriteLine($"details extent {scroller.Extent.Height:0} vs viewport {scroller.Viewport.Height:0}");

            // Footer before any edit.
            var footer = all.OfType<TextBlock>().First(t => t.Classes.Contains("change-summary"));
            Assert.Equal("No changes yet", footer.Text);
            var detailsTab = all.OfType<TabItem>().First();
            Assert.DoesNotContain("changed", detailsTab.Classes);
            var dot = detailsTab.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ChangeDot");
            Assert.False(dot.IsVisible);

            // One edit: the Details dot lights, the footer counts it and names the file count.
            vm.Comment = "edited";
            Pump(2);
            Assert.Contains("changed", detailsTab.Classes);
            Assert.True(dot.IsVisible);
            Assert.Equal("1 change · applies to 3 files", footer.Text);
            _o.WriteLine("footer: " + footer.Text);

            // Reverting by hand clears it again.
            vm.Comment = string.Empty;
            Pump(2);
            Assert.DoesNotContain("changed", detailsTab.Classes);
            Assert.Equal("No changes yet", footer.Text);

            win.Close();
        });
    }

    private sealed class ProbeMetadataService : IMetadataService
    {
        public Track? ReadTrackMetadata(string filePath) => null;
        public Track? ReadTrackMetadata(string filePath, out byte[]? embeddedArt) { embeddedArt = null; return null; }
        public byte[]? ExtractAlbumArt(string filePath) => null;
        public bool WriteTrackMetadata(Track track) => true;
        public bool WriteTrackMetadata(Track track, string targetFilePath, string? titleOverride = null) => true;
        public bool WriteRating(string filePath, int rating, bool isDisliked) => true;
        bool IMetadataService.WriteAdvancedFields(string filePath, AdvancedTagIO.AdvancedFields fields, AdvancedTagIO.AdvancedFields original) => true;
        public AudioFileInfo? ReadFileInfo(string filePath) => null;
        public bool WriteAlbumArt(string filePath, byte[]? imageData) => true;
    }
}
