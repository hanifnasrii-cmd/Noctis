using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Models;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The artist tile used to be a column-wide Button, so the empty gap between two
/// portraits (and the tile's own square corners) opened the artist. Only the circle
/// itself may be clickable: the Button is now the portrait alone with an elliptical
/// clip that the hit-test honours.
/// </summary>
public class ArtistTileHitTestTests
{
    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = Avalonia.Media.FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis.UI/Assets/Icons.axaml") });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis.UI/Assets/Styles.axaml") });
    }

    private static Button? TileAt(Window window, Point p)
    {
        var hit = window.InputHitTest(p) as Visual;
        return hit?.FindAncestorOfType<Button>(includeSelf: true) is { } b && b.Classes.Contains("artist-tile") ? b : null;
    }

    [AvaloniaFact]
    public void OnlyTheCircleOpensTheArtist()
    {
        EnsureAppStyles();
        var vm = new LibraryArtistsViewModel(new FakeLibraryService());
        var row = new ArtistRow();
        for (var i = 0; i < 7; i++)
            row.Artists.Add(new Artist { Id = Guid.NewGuid(), Name = $"Artist {i}", IsFavorite = i == 0 });
        vm.ArtistRows.ReplaceAll(new[] { row });
        var view = new LibraryArtistsView { DataContext = vm };
        var window = new Window { Width = 1400, Height = 900, Content = view };
        window.Show();
        // Row realization is dispatcher-driven; under a full-suite run the queue can be
        // busier than in isolation, so pump until the seven tiles exist (bounded).
        // Hit-testing goes through the compositor's committed tree, so a render tick is
        // required as well, or the probe sees nothing under a loaded suite.
        List<Button> tiles = new();
        for (var i = 0; i < 40 && tiles.Count < 7; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            tiles = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("artist-tile")).ToList();
        }
        Assert.Equal(7, tiles.Count);
        var first = tiles[0];
        var second = tiles[1];
        var b0 = first.Bounds; // in parent coords; translate to window
        var p0 = first.TranslatePoint(new Point(0, 0), window)!.Value;
        var p1 = second.TranslatePoint(new Point(0, 0), window)!.Value;

        // The clickable Button must be exactly the circle, not the whole column.
        Assert.True(b0.Width < 200, $"tile button is {b0.Width}px wide — still column-wide");
        Assert.Equal(b0.Width, b0.Height, 1);

        var centre = new Point(p0.X + b0.Width / 2, p0.Y + b0.Height / 2);
        var gap = new Point((p0.X + b0.Width + p1.X) / 2, centre.Y);
        var corner = new Point(p0.X + 4, p0.Y + 4);
        var belowName = new Point(centre.X, p0.Y + b0.Height + 14);

        Button? hitAtCentre = null;
        for (var i = 0; i < 20 && hitAtCentre == null; i++)
        {
            hitAtCentre = TileAt(window, centre);
            if (hitAtCentre == null)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
        }
        Assert.Same(first, hitAtCentre);
        Assert.Null(TileAt(window, gap));
        Assert.Null(TileAt(window, corner));
        Assert.Null(TileAt(window, belowName));

        // Placeholder glyph (no ImagePath) must sit dead-centre in the 160px circle. It used
        // to be forced into a 56×56 box, which parked the taller-than-wide geometry at the
        // box's left edge (~5px left of centre).
        var glyph = first.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();
        var glyphTopLeft = glyph.TranslatePoint(new Point(0, 0), first)!.Value;
        var rendered = glyph.RenderedGeometry!.Bounds; // in the Path's own coordinates
        var glyphCentre = new Point(glyphTopLeft.X + rendered.X + rendered.Width / 2,
                                    glyphTopLeft.Y + rendered.Y + rendered.Height / 2);
        Assert.Equal(b0.Width / 2, glyphCentre.X, 0.6);
        Assert.Equal(b0.Height / 2, glyphCentre.Y, 0.6);
        Assert.Equal(56, rendered.Height, 0.6);

        // Favourite star badge sits outside the circle; clicks on it fall through, not open.
        var starBadge = first.FindAncestorOfType<Panel>()!.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Width == 26 && b.IsVisible);
        Assert.NotNull(starBadge);
        Assert.False(starBadge!.IsHitTestVisible);
    }
}
