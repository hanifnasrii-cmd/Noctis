using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Noctis.Controls;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Cover Flow carousel cards (09-12 redesign): one CoverFlowCard per slot — a rounded
/// glass frame with the square cover on top and a frosted title/artist caption INSIDE
/// the frame. Side cards tilt toward the centre with a 3D rotate. These pin the card's
/// structure, the five-card row and the tilt orientation (a sign flip would make the
/// cards face away).
/// </summary>
public class CoverFlowCardTests
{
    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis/Assets/Icons.axaml") });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis/Assets/Styles.axaml") });
    }

    [AvaloniaFact]
    public void Card_HasCoverOnTopAndCaptionInsideTheFrame()
    {
        EnsureAppStyles();
        var card = new CoverFlowCard { Title = "a lot", Artist = "21 Savage, J.Cole", ArtworkSize = 300 };
        var window = new Window { Width = 800, Height = 700, Content = card };
        window.Show();
        window.UpdateLayout();

        var frame = card.FindControl<Border>("Frame")!;
        var art = card.FindControl<Panel>("ArtworkPanel")!;
        var caption = card.FindControl<Border>("Caption")!;
        var title = card.FindControl<TextBlock>("TitleText")!;
        var artist = card.FindControl<TextBlock>("ArtistText")!;

        // Frame = artwork + inset + 2px rim each side; the cover sits inset on the slab with
        // the glass margin visible around it, and the caption starts one inset below it.
        Assert.Equal(300 + 2 * CoverFlowCard.ArtworkInset + 2 * CoverFlowCard.RimThickness, frame.Bounds.Width, 1.1);
        Assert.Equal(300, art.Bounds.Width, 0.6);
        Assert.Equal(300, art.Bounds.Height, 0.6);
        var artLeft = art.TranslatePoint(new Point(0, 0), frame)!.Value.X;
        Assert.Equal(CoverFlowCard.RimThickness + CoverFlowCard.ArtworkInset, artLeft, 0.6);
        var artBottom = art.TranslatePoint(new Point(0, art.Bounds.Height), frame)!.Value.Y;
        var captionTop = caption.TranslatePoint(new Point(0, 0), frame)!.Value.Y;
        Assert.Equal(artBottom + CoverFlowCard.ArtworkInset, captionTop, 0.6);
        var artFrame = card.FindControl<Border>("ArtworkFrame")!;
        Assert.True(artFrame.ClipToBounds);
        Assert.Equal(24 - CoverFlowCard.RimThickness - CoverFlowCard.ArtworkInset, artFrame.CornerRadius.TopLeft);
        Assert.True(caption.Bounds.Height is > 60 and < 110, $"caption height {caption.Bounds.Height}");
        Assert.Equal("a lot", title.Text);
        Assert.Equal("21 Savage, J.Cole", artist.Text);
        Assert.True(artist.IsVisible);
        Assert.False(card.FindControl<Button>("ArtistLinkButton")!.IsVisible);

        // The rim is drawn on an UNCLIPPED border (a clipped stroke loses its corner arcs);
        // the content clips one ring inside it at radius − rim; the shadow ring never clips.
        Assert.False(frame.ClipToBounds);
        Assert.Equal(CoverFlowCard.RimThickness, frame.BorderThickness.Left);
        var clip = card.FindControl<Border>("Clip")!;
        Assert.True(clip.ClipToBounds);
        Assert.Equal(24, frame.CornerRadius.TopLeft);
        Assert.Equal(24 - CoverFlowCard.RimThickness, clip.CornerRadius.TopLeft);
        Assert.False(card.FindControl<Border>("Shadow")!.ClipToBounds);
    }

    [AvaloniaFact]
    public void Card_SlabIsOpaqueWithoutLiquidGlass_AndFrostsWithIt()
    {
        EnsureAppStyles();
        var card = new CoverFlowCard { Title = "t", Artist = "a" };
        var glass = card.FindControl<GlassPanel>("CardGlass")!;
        // Off: a plain opaque fill (a see-through slab shows the neighbours as notches).
        Assert.True(glass.UseAppGlass);
        var fill = Assert.IsAssignableFrom<ISolidColorBrush>(glass.Background);
        Assert.Equal(255, fill.Color.A);
        // On: a real backdrop blur by default, lighter tint; a 0-blur card (far slots) is
        // tint-only and heavier so text behind does not read through.
        Assert.Equal(18, glass.BlurRadius);
        Assert.Equal(0.62, glass.GlassTintOpacity!.Value, 3);
        card.CardBlurRadius = 0;
        Assert.Equal(0, glass.BlurRadius);
        Assert.Equal(0.86, glass.GlassTintOpacity!.Value, 3);
        Assert.Equal(0, glass.EdgeThickness);
    }

    [AvaloniaFact]
    public void Card_ArtistBecomesALinkWhenACommandIsGiven()
    {
        EnsureAppStyles();
        var ran = 0;
        var card = new CoverFlowCard { Title = "t", Artist = "a", ArtistCommand = new RelayCommand(() => ran++) };
        var window = new Window { Width = 800, Height = 700, Content = card };
        window.Show();
        window.UpdateLayout();

        var link = card.FindControl<Button>("ArtistLinkButton")!;
        Assert.True(link.IsVisible);
        Assert.False(card.FindControl<TextBlock>("ArtistText")!.IsVisible);
        link.Command!.Execute(null);
        Assert.Equal(1, ran);
    }

    [AvaloniaFact]
    public void Carousel_HasFifteenCards_SideCardsTiltTowardTheCentre()
    {
        EnsureAppStyles();
        var view = new CoverFlowView();
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var cards = view.GetVisualDescendants().OfType<CoverFlowCard>().Where(c => c.Classes.Contains("flow-card")).ToList();
        // Centre + 7 each side so the row spans the page; every card the same size so the
        // caption type matches across the row.
        Assert.Equal(15, cards.Count);
        var centre = view.FindControl<CoverFlowCard>("CenterCard")!;
        Assert.Contains(centre, cards);
        Assert.All(cards, c => Assert.Equal(360, c.ArtworkSize));
        Assert.Null(centre.RenderTransform);
        var left = cards.Where(c => c != centre && c.RenderTransform!.Value.M31 < 0).OrderByDescending(c => c.RenderTransform!.Value.M31).ToList();
        var right = cards.Where(c => c != centre && c.RenderTransform!.Value.M31 > 0).OrderBy(c => c.RenderTransform!.Value.M31).ToList();
        Assert.Equal(7, left.Count);
        Assert.Equal(7, right.Count);
        // Mirror-symmetric slots; scale eases down and dim deepens with distance.
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(-left[i].RenderTransform!.Value.M31, right[i].RenderTransform!.Value.M31, 0.01);
            Assert.Equal(left[i].Dim, right[i].Dim);
            if (i > 0)
            {
                Assert.True(left[i].Dim > left[i - 1].Dim);
                Assert.True(left[i].ZIndex < left[i - 1].ZIndex, "nearer cards draw on top");
            }
        }
        // Live frost on the inner two per side only; the rest are tint-only (cost).
        Assert.All(left.Take(2).Concat(right.Take(2)), c => Assert.Equal(18, c.CardBlurRadius));
        Assert.All(left.Skip(2).Concat(right.Skip(2)), c => Assert.Equal(0, c.CardBlurRadius));
        Assert.Equal(18, centre.CardBlurRadius);

        // Side cards: a perspective matrix. For a card LEFT of centre the edge nearest the
        // centre (its right edge) must be the closer one, i.e. project LARGER (w < 1 there
        // and w > 1 on the far edge); mirrored on the right side.
        foreach (var card in cards.Where(c => c != centre))
        {
            var m = card.RenderTransform!.Value;
            Assert.NotEqual(0, m.M13); // a real 3D tilt, not a flat scale
            double W(double x) => x * m.M13 + m.M33;
            var wRight = W(150);
            var wLeft = W(-150);
            var isLeftOfCentre = m.M31 < 0;
            if (isLeftOfCentre)
                Assert.True(wRight < wLeft, $"left card: right edge w={wRight} should be nearer than left w={wLeft}");
            else
                Assert.True(wLeft < wRight, $"right card: left edge w={wLeft} should be nearer than right w={wRight}");
        }
    }
}
