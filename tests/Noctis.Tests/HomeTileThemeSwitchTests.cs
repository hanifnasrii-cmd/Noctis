using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Models;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Switching to Ink flashed the Home chart rows: their presenter carries an 80ms hover
/// BrushTransition, and a theme switch changes the same Background (HomeCardBackground
/// #18FFFFFF → #1C1C1C). A non-premultiplied lerp between a translucent white and an
/// opaque near-black passes through a much lighter semi-opaque grey. The switch must
/// therefore run with those transitions suppressed.
/// </summary>
public class HomeTileThemeSwitchTests
{
    private static void EnsureAppStyles()
    {
        var app = Application.Current!;
        if (app.Resources.TryGetResource("HeartFillIcon", null, out _)) return;
        app.Resources["InterSemiBold"] = FontFamily.Default;
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/"))
        {
            Source = new Uri("avares://Noctis.UI/Assets/Icons.axaml")
        });
        app.Styles.Add(new StyleInclude(new Uri("avares://Noctis/"))
        {
            Source = new Uri("avares://Noctis.UI/Assets/Styles.axaml")
        });
    }

    private static Track T(string title, int plays) => new()
    {
        Id = Guid.NewGuid(), Title = title, Artist = "A", AlbumArtist = "A", Album = "B",
        AlbumId = Guid.NewGuid(), Duration = TimeSpan.FromSeconds(100), PlayCount = plays,
    };

    private static async Task<(Window Win, ContentPresenter Presenter)> MountChartRow()
    {
        EnsureAppStyles();
        var lib = new FakeLibraryService();
        lib.TrackList.AddRange(new[] { T("a", 5), T("b", 4) });
        var persistence = new TestPersistenceService();
        var player = new PlayerViewModel(new FakeAudioPlayer(), lib, persistence, new FakeAnimatedCoverService());
        var vm = new HomeViewModel(player, lib, new SidebarViewModel(persistence, lib));
        await vm.RefreshAsync();
        var view = new HomeView { DataContext = vm };
        // Dark variant: that is where HomeCardBackground is the translucent white (#18FFFFFF).
        var win = new Window { Width = 1400, Height = 900, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var row = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("home-chart-row"));
        var presenter = row.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        // Warm the tick/sample path before measuring so the first real frames after the swap
        // are not lost to JIT (the evidence test needs several samples inside the 80ms lerp).
        SampleWhileSettling(presenter);
        return (win, presenter);
    }

    /// <summary>Alpha-weighted lightness of the brush over the Ink page (#0F0F0F).</summary>
    private static double Lightness(IBrush? brush)
    {
        var c = ((ISolidColorBrush)brush!).Color;
        var a = c.A / 255d;
        return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) * a + 15 * (1 - a);
    }

    /// <summary>Ink's HomeCardBackground (#1C1C1C) as the lightness the tile must land on.</summary>
    private static double InkCardLightness() =>
        Lightness(ThemeOverlayParityTests.LoadOverlay("Ink")["HomeCardBackground"] as IBrush);

    private static ResourceInclude InkOverlay() =>
        new((Uri?)null) { Source = new Uri("avares://Noctis.UI/Assets/Themes/Ink.axaml") };

    /// <summary>Ticks the headless render timer while real time passes, sampling the presenter
    /// fill each frame; keeps going past the window until the value has held still for a few
    /// frames so a cold (JIT-slow) first iteration cannot end the run mid-transition.</summary>
    private static List<double> SampleWhileSettling(ContentPresenter presenter)
    {
        var samples = new List<double>();
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
        var cap = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        var stillFrames = 0;
        while (DateTime.UtcNow < cap && (DateTime.UtcNow < deadline || stillFrames < 4))
        {
            Thread.Sleep(8);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var v = Lightness(presenter.Background);
            stillFrames = samples.Count > 0 && Math.Abs(samples[^1] - v) < 0.01 ? stillFrames + 1 : 0;
            samples.Add(v);
        }
        return samples;
    }

    [AvaloniaFact(Skip = "Timing evidence of the pre-fix flash: samples a real-time 80ms lerp and misses the mid-frame under full-suite load. Run alone to reproduce.")]
    public async Task Evidence_UnsuppressedSwitch_LerpsThroughLighterGrey()
    {
        var (win, presenter) = await MountChartRow();
        var overlay = InkOverlay();
        try
        {
            var before = Lightness(presenter.Background);
            var target = InkCardLightness();
            Application.Current!.Resources.MergedDictionaries.Add(overlay);
            var samples = SampleWhileSettling(presenter);
            // Only the overshoot is pinned here: the presenter's 80ms transition restarts under
            // the Button's own 60ms one (the fill flows through a TemplateBinding), so where the
            // unsuppressed lerp finally parks is itself unstable — another reason to suppress.
            Assert.True(samples.Max() > Math.Max(before, target) + 10,
                $"expected a lighter mid-frame; before={before:F1} target={target:F1} max={samples.Max():F1}");
        }
        finally
        {
            Application.Current!.Resources.MergedDictionaries.Remove(overlay);
            win.Close();
        }
    }

    [AvaloniaFact]
    public async Task SuppressedSwitch_LandsOnInkFillWithoutAnOvershoot()
    {
        var (win, presenter) = await MountChartRow();
        var overlay = InkOverlay();
        try
        {
            var before = Lightness(presenter.Background);
            Noctis.App.RunWithTransitionsSuppressed(win, () =>
            {
                Assert.Contains(Noctis.App.ThemeSwitchingClass, win.Classes);
                // Both the presenter's hover transition and the global Button one are off.
                Assert.Empty(presenter.Transitions ?? new Transitions());
                Assert.Empty(((Button)presenter.TemplatedParent!).Transitions ?? new Transitions());
                Application.Current!.Resources.MergedDictionaries.Add(overlay);
            });
            var samples = SampleWhileSettling(presenter);
            var after = Lightness(presenter.Background);
            Assert.Equal(InkCardLightness(), after, 1.0);
            Assert.True(samples.Max() <= Math.Max(before, after) + 1,
                $"switch still animated; before={before:F1} after={after:F1} max={samples.Max():F1}");
            // The suppression is transient: the hover transition is back for the next change.
            Assert.DoesNotContain(Noctis.App.ThemeSwitchingClass, win.Classes);
            Assert.NotEmpty(presenter.Transitions ?? new Transitions());
        }
        finally
        {
            Application.Current!.Resources.MergedDictionaries.Remove(overlay);
            win.Close();
        }
    }
}
