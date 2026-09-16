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
/// Cover Flow carousel cards (09-15 redesign): one CoverFlowCard per slot — a rounded
/// LIGHT frosted-glass slab with the square cover inset on top and the title/artist
/// caption inside. Five cards: centre + 2 each side, side cards tilted toward the centre
/// with a 3D rotate and tucked behind their inner neighbour; a skip slides every card
/// from the slot it came from. These pin the card's structure, the row, the tilt
/// orientation (a sign flip would make the cards face away) and the slide.
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
        // 20px card → 19 clip → 15 artwork corners: concentric, so no light pixels leak
        // between the rim, the glass and the clip at the corners.
        Assert.Equal(20 - CoverFlowCard.RimThickness - CoverFlowCard.ArtworkRadiusStep, artFrame.CornerRadius.TopLeft);
        Assert.Equal(15, artFrame.CornerRadius.TopLeft);
        Assert.True(caption.Bounds.Height is > 50 and < 100, $"caption height {caption.Bounds.Height}");
        // Shadow is dark-only with no spread (a light or spread shadow haloes the edge).
        var shadow = card.FindControl<Border>("Shadow")!.BoxShadow[0];
        Assert.Equal(0, shadow.Spread);
        Assert.Equal((byte)0, shadow.Color.R);
        Assert.Equal((byte)0, shadow.Color.G);
        Assert.Equal((byte)0, shadow.Color.B);
        // The artist caption carries the app accent colour (it was a muted white).
        Assert.True(Application.Current!.TryGetResource("AccentColorBrush", card.ActualThemeVariant, out var accent));
        var accentColor = Assert.IsAssignableFrom<ISolidColorBrush>(accent).Color;
        Assert.Equal(accentColor, Assert.IsAssignableFrom<ISolidColorBrush>(artist.Foreground).Color);
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
        Assert.Equal(20, frame.CornerRadius.TopLeft);
        Assert.Equal(19, clip.CornerRadius.TopLeft);
        Assert.Equal(20 - CoverFlowCard.RimThickness, clip.CornerRadius.TopLeft);
        Assert.False(card.FindControl<Border>("Shadow")!.ClipToBounds);
    }

    [AvaloniaFact]
    public void Card_SlabIsRealTranslucentGlass_RegardlessOfTheAppSwitch()
    {
        EnsureAppStyles();
        var card = new CoverFlowCard { Title = "t", Artist = "a" };
        var glass = card.FindControl<GlassPanel>("CardGlass")!;
        // Always frosted: the reference is a see-through frosted card, so the slab does not
        // follow the app-wide Liquid Glass toggle and has no flat fallback fill.
        Assert.False(glass.UseAppGlass);
        Assert.True(glass.IsGlassActive);
        Assert.True(glass.EffectiveGlassActive);
        Assert.Null(glass.Background);
        // A real backdrop blur under a SMOKY GREY tint (white read milky); a 0-blur card is
        // tint-only and heavier.
        Assert.Equal(Color.Parse("#808080"), glass.GlassTint);
        Assert.Equal(18, glass.BlurRadius);
        Assert.Equal(0.40, glass.GlassTintOpacity!.Value, 3);
        card.CardBlurRadius = 0;
        Assert.Equal(0, glass.BlurRadius);
        Assert.Equal(0.72, glass.GlassTintOpacity!.Value, 3);
        Assert.Equal(0, glass.EdgeThickness);
        // Rim: 1px at 12% white — barely visible, never a white outline. The only lit edge
        // is a 1px top highlight inside the clip. Caption type caps at 20/16 on a 360px card.
        var frame = card.FindControl<Border>("Frame")!;
        Assert.Equal(1, frame.BorderThickness.Left);
        var rim = ((ISolidColorBrush)frame.BorderBrush!).Color;
        Assert.Equal((255, 255, 255), (rim.R, rim.G, rim.B));
        Assert.InRange(rim.A, 20, 40);
        var highlight = card.FindControl<Border>("TopHighlight")!;
        Assert.Equal(1, highlight.Height);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, highlight.VerticalAlignment);
        var lit = ((ISolidColorBrush)highlight.Background!).Color;
        Assert.InRange(lit.A, 10, 30);
        card.ArtworkSize = 360;
        Assert.Equal(20, card.TitleFontSize);
        Assert.Equal(16, card.ArtistFontSize);
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
    public void Carousel_HasFiveCards_SideCardsTiltTowardTheCentre_AndTuckBehind()
    {
        EnsureAppStyles();
        var view = new CoverFlowView();
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var cards = view.GetVisualDescendants().OfType<CoverFlowCard>().Where(c => c.Classes.Contains("flow-card")).ToList();
        // Centre + 2 each side — only five controls are ever realised.
        Assert.Equal(5, cards.Count);
        var centre = view.FindControl<CoverFlowCard>("CenterCard")!;
        Assert.Contains(centre, cards);
        Assert.All(cards, c => Assert.Equal(360, c.ArtworkSize));
        Assert.All(cards, c => Assert.Equal(18, c.CardBlurRadius));

        // Centre: faces the viewer, unwashed, on top.
        var cm = centre.RenderTransform!.Value;
        Assert.Equal(0, cm.M13, 9);
        Assert.Equal(1, cm.M11, 9);
        Assert.Equal(0, cm.M31, 9);
        Assert.Equal(0, centre.Dim);
        Assert.Equal(1, centre.Opacity);

        var left = new[] { view.FindControl<CoverFlowCard>("SlotPrev1")!, view.FindControl<CoverFlowCard>("SlotPrev2")! };
        var right = new[] { view.FindControl<CoverFlowCard>("SlotNext1")!, view.FindControl<CoverFlowCard>("SlotNext2")! };
        for (var i = 0; i < 2; i++)
        {
            var l = left[i].RenderTransform!.Value;
            var r = right[i].RenderTransform!.Value;
            Assert.True(l.M31 < 0 && r.M31 > 0);
            Assert.Equal(-l.M31, r.M31, 0.01);
            Assert.Equal(left[i].Dim, right[i].Dim);
            Assert.Equal(left[i].ZIndex, right[i].ZIndex);
            Assert.True(left[i].ZIndex < centre.ZIndex, "the centre card draws on top");
            if (i > 0)
            {
                Assert.True(left[i].Dim > left[i - 1].Dim, "outer cards are dimmer");
                Assert.True(left[i].ZIndex < left[i - 1].ZIndex, "each card tucks behind its inner neighbour");
                Assert.True(left[i].Opacity < left[i - 1].Opacity, "outer cards fade a little");
            }
        }

        // Side cards: a perspective matrix. For a card LEFT of centre the edge nearest the
        // centre (its right edge) must be the closer one, i.e. project LARGER (w < 1 there
        // and w > 1 on the far edge); mirrored on the right side.
        foreach (var card in left.Concat(right))
        {
            var m = card.RenderTransform!.Value;
            Assert.NotEqual(0, m.M13); // a real 3D tilt, not a flat scale
            double W(double x) => x * m.M13 + m.M33;
            var wRight = W(150);
            var wLeft = W(-150);
            if (m.M31 < 0)
                Assert.True(wRight < wLeft, $"left card: right edge w={wRight} should be nearer than left w={wLeft}");
            else
                Assert.True(wLeft < wRight, $"right card: left edge w={wLeft} should be nearer than right w={wRight}");
        }
    }

    [AvaloniaFact]
    public void Carousel_SkipSlidesEveryCardFromTheSlotItCameFrom()
    {
        EnsureAppStyles();
        var view = new CoverFlowView();
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Forward one: the new centre card (content already swapped by binding) starts
        // where the +1 card was and eases home; the new −1 starts at the centre pose; the
        // new +2 comes in from the transparent exit slot.
        view.OnCarouselShifted(null, 1);
        Assert.True(view.IsSliding);
        view.ApplySlideFrame(0);
        Assert.Equal(1, view.PositionOf(0), 2);
        Assert.Equal(0, view.PositionOf(-1), 2);
        Assert.Equal(3, view.PositionOf(2), 2);
        Assert.Equal(0, view.FindControl<CoverFlowCard>("SlotNext2")!.Opacity);

        view.ApplySlideFrame(0.5);
        Assert.Equal(0.5, view.PositionOf(0), 2);
        Assert.Equal(-0.5, view.PositionOf(-1), 2);
        Assert.True(view.FindControl<CoverFlowCard>("SlotNext2")!.Opacity > 0);

        view.ApplySlideFrame(1);
        Assert.Equal(0, view.PositionOf(0), 2);
        Assert.Equal(-1, view.PositionOf(-1), 2);
        Assert.Equal(2, view.PositionOf(2), 2);

        // Back two: the new centre came from the −2 slot.
        view.OnCarouselShifted(null, -2);
        view.ApplySlideFrame(0);
        Assert.Equal(-2, view.PositionOf(0), 2);
        Assert.Equal(-3, view.PositionOf(-1), 2);
        view.ApplySlideFrame(1);
        Assert.Equal(0, view.PositionOf(0), 2);

        // Step 0 (a track from outside the row) snaps, no slide.
        view.OnCarouselShifted(null, 0);
        Assert.False(view.IsSliding);
        Assert.Equal(1, view.PositionOf(1), 2);
    }
}
