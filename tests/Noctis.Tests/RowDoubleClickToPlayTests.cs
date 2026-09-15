using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Models;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The Home charts (Most Played / Last Played) and the artist page song rows play on a
/// DOUBLE click (user ask 09-14), matching every flat track list in the app. Both rows used
/// to carry a Button.Command, so a single click played.
///
/// Headless input delivers only the first click of a pair and never raises DoubleTapped
/// (probed 09-14), so the two halves are covered separately: a real simulated click proves
/// nothing plays, and a raised Gestures.DoubleTapped proves the handler wired in the XAML
/// plays the right track.
/// </summary>
public class RowDoubleClickToPlayTests
{
    private static void Pump(int n = 4)
    {
        for (var i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }

    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/"))
        { Source = new Uri("avares://Noctis/Assets/Icons.axaml") });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/"))
        { Source = new Uri("avares://Noctis/Assets/Styles.axaml") });
    }

    /// <summary>Raises the routed event the gesture recognizer raises on a double click, so
    /// the handler wired in the XAML runs for real.</summary>
    private static void DoubleTap(Control target, Visual root)
    {
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root) ?? default;
        var press = new PointerPressedEventArgs(
            target, pointer, root, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None, 2);
        target.RaiseEvent(new TappedEventArgs(Gestures.DoubleTappedEvent, press));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SingleClick(Window win, Control target)
    {
        var pt = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), win)!.Value;
        win.MouseMove(pt); Pump(1);
        win.MouseDown(pt, MouseButton.Left); Pump(1);
        win.MouseUp(pt, MouseButton.Left); Pump(2);
    }

    // ---- Home: Most Played / Last Played ----

    private static async Task<(PlayerViewModel Player, HomeView View, Window Win, Track First)> MountHome()
    {
        EnsureAppStyles();
        var lib = new FakeLibraryService();
        var albumId = Guid.NewGuid();
        Track T(string title, int plays) => new()
        {
            Id = Guid.NewGuid(), Title = title, Artist = "Chase Atlantic", AlbumArtist = "Chase Atlantic",
            Album = "Phases", AlbumId = albumId, Duration = TimeSpan.FromSeconds(180), PlayCount = plays,
        };
        var a = T("INTRO", 78);
        var b = T("ANGELS", 73);
        lib.TrackList.AddRange(new[] { a, b });
        ((List<Album>)lib.Albums).Add(new Album
        { Id = albumId, Name = "Phases", Artist = "Chase Atlantic", Tracks = new List<Track> { a, b } });
        var persistence = new TestPersistenceService();
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, persistence, new FakeAnimatedCoverService());
        var vm = new HomeViewModel(player, lib, new SidebarViewModel(persistence, lib));
        await vm.RefreshAsync();
        var view = new HomeView { DataContext = vm };
        var win = new Window { Width = 1400, Height = 900, Content = view };
        win.Show(); Pump();
        return (player, view, win, a);
    }

    private static List<Button> ChartRows(HomeView view) =>
        view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("home-chart-row")).ToList();

    [AvaloniaFact]
    public async Task HomeChartRow_SingleClick_DoesNotPlay()
    {
        var (player, view, win, _) = await MountHome();
        var row = ChartRows(view).First();

        SingleClick(win, row);

        Assert.Null(player.CurrentTrack);
    }

    [AvaloniaFact]
    public async Task HomeChartRow_DoubleClick_PlaysThatTrack()
    {
        var (player, view, win, first) = await MountHome();
        var row = ChartRows(view).First();
        Assert.Same(first, ((TopSongRow)row.DataContext!).Track);

        DoubleTap(row, win);

        Assert.Same(first, player.CurrentTrack);
    }

    // ---- Artist page: Top Songs / Top Favorites / Songs ----

    private static (PlayerViewModel Player, ArtistDetailView View, Window Win) MountArtist()
    {
        EnsureAppStyles();
        var lib = new FakeLibraryService();
        var albumId = Guid.NewGuid();
        var album = new Album { Id = albumId, Name = "Phases", Artist = "Chase Atlantic", Year = 2019, Tracks = new List<Track>() };
        for (var i = 1; i <= 5; i++)
            album.Tracks.Add(new Track
            {
                Id = Guid.NewGuid(), Title = $"Song {i}", Artist = "Chase Atlantic", AlbumArtist = "Chase Atlantic",
                Album = "Phases", AlbumId = albumId, TrackNumber = i, Year = 2019,
                Duration = TimeSpan.FromMinutes(3), PlayCount = 20 - i,
            });
        album.TrackCount = 5;
        ((List<Album>)lib.Albums).Add(album);
        var persistence = new TestPersistenceService();
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, persistence, new FakeAnimatedCoverService());
        var vm = new ArtistDetailViewModel("Chase Atlantic", lib, player);
        var view = new ArtistDetailView { DataContext = vm };
        var win = new Window { Width = 1280, Height = 900, Content = view };
        win.Show(); Pump();
        return (player, view, win);
    }

    private static List<Button> SongRows(ArtistDetailView view) =>
        view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("song-row")).ToList();

    [AvaloniaFact]
    public void ArtistSongRow_SingleClick_DoesNotPlay()
    {
        var (player, view, win) = MountArtist();
        var row = SongRows(view).First();

        SingleClick(win, row);

        Assert.Null(player.CurrentTrack);
    }

    [AvaloniaFact]
    public void ArtistSongRow_DoubleClick_PlaysThatTrack()
    {
        var (player, view, win) = MountArtist();
        var row = SongRows(view).Skip(1).First();
        var track = ((TopSongRow)row.DataContext!).Track;

        DoubleTap(row, win);

        Assert.Same(track, player.CurrentTrack);
    }

    [AvaloniaFact]
    public void ArtistSongRow_DoubleClickOnTheRowMenuGlyph_DoesNotPlay()
    {
        var (player, view, win) = MountArtist();
        var row = SongRows(view).First();
        var menuGlyph = row.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("row-menu-btn"));

        // Raised on the glyph, so it bubbles to the row with the glyph as its source.
        DoubleTap(menuGlyph, win);

        Assert.Null(player.CurrentTrack);
    }
}
