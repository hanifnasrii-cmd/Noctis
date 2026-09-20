using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// WCAG AA for the six new overlays: every body-text / surface pair the app actually draws
/// (page, sidebar, cards, island, queue drawer, selected sidebar pill, accent buttons, the
/// now-playing row) clears 4.5:1; tertiary text clears 3:1. Translucent surfaces are
/// composited over the window background first, the way the screen shows them.
/// </summary>
public class ThemeContrastTests
{
    public static IEnumerable<object[]> Themes() => ThemeOverlayParityTests.NewThemeNames();

    private static Color ColorOf(ResourceDictionary d, string key)
    {
        Assert.True(d.TryGetValue(key, out var v), $"missing {key}");
        return v switch
        {
            Color c => c,
            ISolidColorBrush b => b.Color,
            // Gradient window backgrounds: use the mid-point of the stops as the reference tone.
            IGradientBrush g => Average(g.GradientStops.Select(s => s.Color)),
            _ => throw new Xunit.Sdk.XunitException($"{key} is a {v?.GetType().Name}, not a colour"),
        };
    }

    private static Color Average(IEnumerable<Color> colors)
    {
        var list = colors.ToList();
        return Color.FromRgb(
            (byte)list.Average(c => c.R), (byte)list.Average(c => c.G), (byte)list.Average(c => c.B));
    }

    /// <summary>Source-over composite of <paramref name="top"/> on an opaque <paramref name="under"/>.</summary>
    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(top.R * a + under.R * (1 - a)),
            (byte)Math.Round(top.G * a + under.G * (1 - a)),
            (byte)Math.Round(top.B * a + under.B * (1 - a)));
    }

    private static double Luminance(Color c)
    {
        static double Lin(byte ch)
        {
            var v = ch / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static double Contrast(Color fg, Color bg)
    {
        var f = Over(fg, bg);
        var hi = Math.Max(Luminance(f), Luminance(bg));
        var lo = Math.Min(Luminance(f), Luminance(bg));
        return (hi + 0.05) / (lo + 0.05);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void TextOnEverySurface_MeetsWcagAA(string theme)
    {
        var d = ThemeOverlayParityTests.LoadOverlay(theme);
        var black = Colors.Black;
        var window = Over(ColorOf(d, "AppWindowBackgroundBrush"), black);
        var main = Over(ColorOf(d, "AppMainBackground"), window);
        var sidebar = Over(ColorOf(d, "AppSidebarBackground"), window);
        var card = Over(ColorOf(d, "HomeCardBackground"), main);
        var island = Over(ColorOf(d, "IslandBackground"), main);
        var queue = Over(ColorOf(d, "QueueDrawerBackground"), main);
        var selected = Over(ColorOf(d, "SidebarSelectedBrush"), sidebar);
        var accent = Over(ColorOf(d, "AccentColorBrush"), black);
        var nowPlaying = Over(ColorOf(d, "NowPlayingRowBrush"), black);

        var high = ColorOf(d, "SystemControlForegroundBaseHighBrush");
        var medium = ColorOf(d, "SystemControlForegroundBaseMediumBrush");
        var mediumLow = ColorOf(d, "SystemControlForegroundBaseMediumLowBrush");
        var secondary = ColorOf(d, "SecondaryTextBrush");
        var tertiary = ColorOf(d, "TertiaryTextBrush");

        var failures = new List<string>();
        void Check(string label, Color fg, Color bg, double floor)
        {
            var r = Contrast(fg, bg);
            if (r < floor) failures.Add($"{label}: {r:0.00} < {floor}");
        }

        foreach (var (name, surface) in new[] { ("page", main), ("sidebar", sidebar), ("card", card) })
        {
            Check($"primary text on {name}", high, surface, 4.5);
            Check($"medium text on {name}", medium, surface, 4.5);
            Check($"secondary text on {name}", secondary, surface, 4.5);
            Check($"tertiary text on {name}", tertiary, surface, 3.0);
            Check($"medium-low text on {name}", mediumLow, surface, 3.0);
        }
        Check("island text", ColorOf(d, "IslandForeground"), island, 4.5);
        Check("island secondary", ColorOf(d, "IslandForegroundSecondary"), island, 4.5);
        Check("island tertiary", ColorOf(d, "IslandForegroundTertiary"), island, 3.0);
        Check("queue drawer text", ColorOf(d, "QueueDrawerForeground"), queue, 4.5);
        Check("primary text on selected sidebar pill", high, selected, 4.5);
        Check("accent button text", ColorOf(d, "AccentForegroundBrush"), accent, 4.5);
        Check("now-playing row text", ColorOf(d, "NowPlayingRowForegroundBrush"), nowPlaying, 4.5);
        Check("accent text on page", ColorOf(d, "AccentTextBrush"), main, 3.0);

        Assert.True(failures.Count == 0, $"{theme}:\n  " + string.Join("\n  ", failures));
    }
}
