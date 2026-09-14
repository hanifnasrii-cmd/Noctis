using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Models;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Headless mount of the Home page after the 09-13 "Continue listening" layout: pins
/// that the XAML resolves its resources/bindings at runtime and that the hero, the two
/// side-by-side charts and the compact rows realize.
/// </summary>
public class HomeViewMountTests
{
    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = Avalonia.Media.FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/"))
        {
            Source = new Uri("avares://Noctis/Assets/Icons.axaml")
        });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/"))
        {
            Source = new Uri("avares://Noctis/Assets/Styles.axaml")
        });
    }

    private static readonly Guid AlbumId = Guid.NewGuid();

    private static Track T(string title, string artist, int plays) => new()
    {
        Id = Guid.NewGuid(), Title = title, Artist = artist, AlbumArtist = artist, Album = "Un Verano Sin Ti",
        AlbumId = AlbumId, Duration = TimeSpan.FromSeconds(176), PlayCount = plays,
    };

    [AvaloniaFact]
    public async Task HomePage_MountsHeroAndSideBySideCharts()
    {
        EnsureAppStyles();
        var lib = new FakeLibraryService();
        var a = T("Un Ratito", "Bad Bunny", 56);
        var b = T("ANGELS", "Chase Atlantic", 55);
        var c = T("INTRO", "Chase Atlantic", 54);
        lib.TrackList.AddRange(new[] { a, b, c });
        ((List<Album>)lib.Albums).Add(new Album
        {
            Id = AlbumId, Name = "Un Verano Sin Ti", Artist = "Bad Bunny", Tracks = new List<Track> { a, b, c },
        });
        var persistence = new TestPersistenceService();
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, persistence, new FakeAnimatedCoverService());
        player.History.Add(a);
        player.History.Add(b);
        var vm = new HomeViewModel(player, lib, new SidebarViewModel(persistence, lib));
        await vm.RefreshAsync();

        var view = new HomeView { DataContext = vm };
        var win = new Window { Width = 1400, Height = 900, Content = view };
        win.Show();
        Dispatcher.UIThread.RunJobs();

        // Hero: the newest history entry, its kicker, the album after the artist (no year
        // tagged here), and the Resume button (nothing is playing).
        Assert.Same(a, vm.ContinueTrack);
        Assert.Equal(" · Un Verano Sin Ti", vm.ContinueDetail);
        Assert.False(vm.IsContinuePlaying);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Continue listening", texts);
        Assert.Contains("Resume", texts);
        Assert.Contains("Most Played", texts);
        Assert.Contains("Last Played", texts);
        var hero = view.FindControl<Border>("ContinueHero");
        Assert.NotNull(hero);
        Assert.True(hero!.IsVisible);

        // Charts sit side by side (two star columns) at this width.
        var grid = view.FindControl<Grid>("HeroGrid")!;
        Assert.True(grid.ColumnDefinitions[2].Width.IsStar);
        Assert.Equal(2, Grid.GetColumn(view.FindControl<StackPanel>("LastPlayedPanel")!));

        // Albums row: one recently played album; covers use the Albums-page maths (Auto =
        // five across the row's own measured width, 2px slack) and carry the hover overlay.
        var row = view.FindControl<ItemsControl>("AlbumsRow")!;
        Assert.True(row.Bounds.Width > 0);
        Assert.Equal(Noctis.Helpers.AlbumGridMetrics.ComputeTileSize(row.Bounds.Width - 2, 5), vm.AlbumTileSize, precision: 0);
        var tiles = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("album-tile")).ToList();
        Assert.Single(tiles);
        Assert.Single(tiles[0].GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("tile-play"));
        Assert.Single(tiles[0].GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("tile-more"));

        // Compact rows: 3 Most Played + 2 Last Played, none with a dots button.
        var rows = view.GetVisualDescendants().OfType<Button>().Where(r => r.Classes.Contains("home-chart-row")).ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(0, rows.Sum(r => r.GetVisualDescendants().OfType<Button>().Count(x => x.Classes.Contains("chart-dots"))));
    }
}
