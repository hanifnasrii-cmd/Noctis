using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The Ink / Smoke overlays must define every key the base Dark dictionary in
/// Assets/Styles.axaml defines (plus the Fluent foreground brushes and toggle fills they take
/// over), so no DynamicResource consumer ever falls through to a base value tuned for Gray.
/// </summary>
public class ThemeOverlayParityTests
{
    public static readonly string[] NewThemes =
    {
        "Ink", "Smoke",
    };

    public static IEnumerable<object[]> NewThemeNames() => NewThemes.Select(n => new object[] { n });

    /// <summary>Keys the base Dark dictionary leaves to Fluent / SetAccent but the new overlays own.</summary>
    private static readonly string[] ExtraRequired =
    {
        "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchFillOnDragging",
        "SystemControlForegroundBaseHighBrush", "SystemControlForegroundBaseMediumBrush",
        "SystemControlForegroundBaseMediumLowBrush", "SystemControlForegroundBaseLowBrush",
    };

    public static ResourceDictionary LoadOverlay(string name)
    {
        var include = new ResourceInclude(new Uri("avares://Noctis/"))
        {
            Source = new Uri($"avares://Noctis.UI/Assets/Themes/{name}.axaml"),
        };
        return Assert.IsType<ResourceDictionary>(include.Loaded);
    }

    public static IReadOnlyList<string> BaseDarkKeys()
    {
        // Styles.axaml resolves the app font and icon geometries with StaticResource; the
        // headless app carries neither, so provide them the way the view-mount tests do.
        var app = Avalonia.Application.Current!;
        if (!app.Resources.ContainsKey("InterSemiBold"))
            app.Resources["InterSemiBold"] = Avalonia.Media.FontFamily.Default;
        if (!app.Resources.TryGetResource("HeartFillIcon", null, out _))
            app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Noctis/"))
            {
                Source = new Uri("avares://Noctis.UI/Assets/Icons.axaml"),
            });
        var include = new StyleInclude(new Uri("avares://Noctis/"))
        {
            Source = new Uri("avares://Noctis.UI/Assets/Styles.axaml"),
        };
        var styles = Assert.IsType<Styles>(include.Loaded);
        var dark = Assert.IsType<ResourceDictionary>(styles.Resources.ThemeDictionaries[ThemeVariant.Dark]);
        return dark.Keys.OfType<string>().ToList();
    }

    [AvaloniaTheory]
    [MemberData(nameof(NewThemeNames))]
    public void Overlay_DefinesEveryBaseKey(string theme)
    {
        var overlay = LoadOverlay(theme);
        var missing = BaseDarkKeys().Concat(ExtraRequired)
            .Where(k => !overlay.ContainsKey(k))
            .ToList();
        Assert.True(missing.Count == 0, $"{theme} is missing: {string.Join(", ", missing)}");
    }

    [AvaloniaFact]
    public void Overlays_ShareOneKeySet_UpToTheirDeclaredExtras()
    {
        // Every overlay carries the same key set.
        var optional = new HashSet<string>();
        var sets = NewThemes.ToDictionary(n => n, n => LoadOverlay(n).Keys.OfType<string>().Where(k => !optional.Contains(k)).ToHashSet());
        var reference = sets["Ink"];
        foreach (var (name, keys) in sets)
        {
            Assert.True(keys.SetEquals(reference),
                $"{name} differs from Ink: +[{string.Join(",", keys.Except(reference))}] -[{string.Join(",", reference.Except(keys))}]");
        }
    }
}
