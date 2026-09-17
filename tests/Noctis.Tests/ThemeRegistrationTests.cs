using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// App.SetTheme wiring for the six new built-in themes, the two theme-declared accent
/// behaviours SetAccent honours (fixed now-playing row, gradient action buttons), and the
/// inline-brush DynamicResource the playback island now relies on.
/// </summary>
public class ThemeRegistrationTests
{
    private static Noctis.App? s_app;

    /// <summary>A real Noctis.App, initialised once (Initialize registers global handlers).
    /// Application.Current stays the headless test app; this one is only probed.</summary>
    private static Noctis.App RealApp()
    {
        if (s_app == null)
        {
            var app = new Noctis.App();
            app.Initialize();
            s_app = app;
        }
        return s_app;
    }

    private static ResourceInclude? ThemeOverlayOf(Noctis.App app) =>
        app.Resources.MergedDictionaries.OfType<ResourceInclude>()
            .SingleOrDefault(r => r.Source?.ToString().Contains("/Assets/Themes/") == true);

    private static Color Resolve(Noctis.App app, string key)
    {
        Assert.True(app.TryGetResource(key, app.RequestedThemeVariant, out var v), $"'{key}' not found");
        return AccentTestHarness.ColorOf(v as IBrush);
    }

    [AvaloniaTheory]
    [InlineData("HazeLight", true)]
    [InlineData("HazeDark", false)]
    [InlineData("EditorialLight", true)]
    [InlineData("EditorialDark", false)]
    [InlineData("GlassLight", true)]
    [InlineData("GlassDark", false)]
    public void SetTheme_MergesOverlayAndPicksVariant(string name, bool light)
    {
        var app = RealApp();
        app.SetTheme(name);

        Assert.Equal(light ? ThemeVariant.Light : ThemeVariant.Dark, app.RequestedThemeVariant);
        var overlay = ThemeOverlayOf(app);
        Assert.NotNull(overlay);
        Assert.EndsWith($"/Assets/Themes/{name}.axaml", overlay!.Source!.ToString());

        // The overlay's surface wins over the base dictionary's.
        var expected = AccentTestHarness.ColorOf(ThemeOverlayParityTests.LoadOverlay(name)["AppMainBackground"] as IBrush);
        Assert.Equal(expected, Resolve(app, "AppMainBackground"));

        // Switching back removes the overlay and restores Gray's surface.
        app.SetTheme(Noctis.App.ThemeGray);
        Assert.Null(ThemeOverlayOf(app));
        Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
        Assert.Equal(Color.Parse("#252525"), Resolve(app, "AppMainBackground"));
    }

    [AvaloniaFact]
    public void Editorial_PinsNowPlayingRow_WhileGrayFollowsAccent()
    {
        var app = RealApp();
        app.SetTheme(Noctis.App.ThemeEditorialLight);
        app.SetAccent("#874CF2");
        Assert.Equal(Color.Parse("#2B66D9"), Resolve(app, "NowPlayingRowBrush"));
        Assert.Equal(Colors.White, Resolve(app, "NowPlayingRowForegroundBrush"));

        app.SetTheme(Noctis.App.ThemeGray);
        app.SetAccent("#874CF2");
        Assert.Equal(Color.Parse("#874CF2"), Resolve(app, "NowPlayingRowBrush"));
    }

    [AvaloniaFact]
    public void Haze_BuildsGradientActionButtonFromTheLiveAccent()
    {
        var app = RealApp();
        app.SetTheme(Noctis.App.ThemeHazeDark);
        app.SetAccent("#12C76F");
        Assert.True(app.Resources.TryGetResource("AccentButtonBackground", app.RequestedThemeVariant, out var v));
        var gradient = Assert.IsAssignableFrom<IGradientBrush>(v);
        Assert.Equal(Color.Parse("#12C76F"), gradient.GradientStops[0].Color);
        Assert.NotEqual(gradient.GradientStops[0].Color, gradient.GradientStops[^1].Color);

        app.SetTheme(Noctis.App.ThemeGray);
        app.SetAccent("#12C76F");
        Assert.True(app.Resources.TryGetResource("AccentButtonBackground", app.RequestedThemeVariant, out var solid));
        Assert.Equal(Color.Parse("#12C76F"), AccentTestHarness.ColorOf(solid as IBrush));
    }

    /// <summary>
    /// PlaybackBarView's island fill is an inline SolidColorBrush whose Color is a
    /// DynamicResource (the user-opacity binding lives on the same brush). This mounts the
    /// real bar and pins that the fill re-resolves when the theme colour changes at runtime.
    /// </summary>
    [AvaloniaFact]
    public void IslandFill_FollowsThemeColour_AtRuntime()
    {
        var app = Application.Current!;
        app.Resources["IslandBackgroundColor"] = Color.Parse("#202020");
        var player = new Noctis.ViewModels.PlayerViewModel(
            new FakeAudioPlayer(), new FakeLibraryService(),
            new TestPersistenceService(), new FakeAnimatedCoverService());
        var bar = new Noctis.Views.PlaybackBarView { DataContext = player };
        var win = new Window { Width = 900, Height = 200, Content = bar };
        try
        {
            win.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var island = Assert.IsType<Border>(bar.FindControl<Border>("IslandBorder"));
            var glass = Assert.IsType<Noctis.Controls.GlassPanel>(island.Child);
            var brush = Assert.IsType<SolidColorBrush>(glass.Background);
            Assert.Equal(Color.Parse("#202020"), brush.Color);

            app.Resources["IslandBackgroundColor"] = Color.Parse("#FFFFFF");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FFFFFF"), brush.Color);
        }
        finally
        {
            win.Close();
            app.Resources.Remove("IslandBackgroundColor");
        }
    }
}
