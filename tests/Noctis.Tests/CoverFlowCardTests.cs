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
/// structure and the tilt orientation (a sign flip would make the cards face away).
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

        // Frame = artwork + 1.5px edge each side; caption sits directly under the square cover.
        Assert.Equal(303.5, frame.Bounds.Width, 1.1); // 300 + 1.5px edge each side, layout-rounded
        Assert.Equal(300, art.Bounds.Width, 0.6);
        Assert.Equal(300, art.Bounds.Height, 0.6);
        var artBottom = art.TranslatePoint(new Point(0, art.Bounds.Height), frame)!.Value.Y;
        var captionTop = caption.TranslatePoint(new Point(0, 0), frame)!.Value.Y;
        Assert.Equal(artBottom, captionTop, 0.6);
        Assert.True(caption.Bounds.Height is > 40 and < 90, $"caption height {caption.Bounds.Height}");
        Assert.Equal("a lot", title.Text);
        Assert.Equal("21 Savage, J.Cole", artist.Text);
        Assert.True(artist.IsVisible);
        Assert.False(card.FindControl<Button>("ArtistLinkButton")!.IsVisible);

        // The frame clips to its rounded corners; the shadow border outside it does not.
        Assert.True(frame.ClipToBounds);
        Assert.False(card.FindControl<Border>("Shadow")!.ClipToBounds);
        Assert.Equal(18, frame.CornerRadius.TopLeft);
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
        Assert.Equal(15, cards.Count);
        var centre = view.FindControl<CoverFlowCard>("CenterCard")!;
        Assert.Contains(centre, cards);
        Assert.Equal(360, centre.ArtworkSize);
        Assert.Null(centre.RenderTransform);

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
