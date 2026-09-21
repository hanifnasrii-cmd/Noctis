using System.Diagnostics;
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
/// Wheel scrolling the Artists grid skipped whenever the glide crossed into a new row: the
/// virtualizing panel detaches a recycled row and every ContentPresenter in it rebuilds on
/// re-attach (Avalonia design), so each new row re-instantiates seven tiles. Probed at 20ms
/// average / 56ms worst per half-notch step, which the wall-clock glide renders as a jump.
/// Two PathIcons and the inline-runs text block were the bulk of it (09-12). This prints the
/// same probe so a regression shows in the test log; it is not a timing bound (CI noise).
/// </summary>
public class ArtistGridScrollCostTests
{
    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = Avalonia.Media.FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis.UI/Assets/Icons.axaml") });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis.UI/Assets/Styles.axaml") });
    }

    [AvaloniaFact]
    public void ScrollStep_LayoutCost_Report()
    {
        EnsureAppStyles();
        var vm = new LibraryArtistsViewModel(new FakeLibraryService());
        var rows = new List<ArtistRow>();
        for (var r = 0; r < 60; r++)
        {
            var row = new ArtistRow();
            for (var i = 0; i < 7; i++)
                row.Artists.Add(new Artist { Id = Guid.NewGuid(), Name = $"Artist {r * 7 + i}", IsFavorite = i == 3 });
            rows.Add(row);
        }
        vm.ArtistRows.ReplaceAll(rows);
        var view = new LibraryArtistsView { DataContext = vm };
        var window = new Window { Width = 1400, Height = 900, Content = view };
        window.Show();
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);

        double max = 0, sum = 0; var n = 0;
        for (var y = 0.0; y < scroller.Extent.Height - scroller.Viewport.Height; y += 110)
        {
            var sw = Stopwatch.StartNew();
            scroller.Offset = new Vector(0, y);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var ms = sw.Elapsed.TotalMilliseconds;
            max = Math.Max(max, ms); sum += ms; n++;
        }
        Console.WriteLine($"PROBE artists grid: {n} half-notch steps, avg {sum / n:F2}ms, max {max:F1}ms");

        // The tiles still render what they should after the swap to Path shapes.
        var tiles = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("artist-tile")).ToList();
        Assert.True(tiles.Count >= 7);
        Assert.All(tiles, t => Assert.IsType<Artist>(t.DataContext));
    }
}
