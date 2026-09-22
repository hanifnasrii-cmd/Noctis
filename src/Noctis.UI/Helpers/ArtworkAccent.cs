using Avalonia.Media;

namespace Noctis.Helpers;

/// <summary>
/// "Accent follows album art" (Settings → Appearance): turns the cover's vibrant colour
/// into one that can carry buttons, links and the now-playing row. The share-card vibrant
/// colour is clamped for a dark background; an accent needs mid lightness and real chroma.
/// </summary>
public static class ArtworkAccent
{
    private const double MinSaturation = 0.5;
    private const double MinLightness = 0.42;
    private const double MaxLightness = 0.6;
    /// <summary>Below this the cover is effectively grey: a grey accent reads as broken, so callers keep the user's own accent.</summary>
    private const double GreyThreshold = 0.12;

    /// <summary>Returns a legible accent hex derived from the artwork colour, or null when the cover is too grey to use.</summary>
    public static string? TameForAccent(string? artworkHex)
    {
        if (string.IsNullOrWhiteSpace(artworkHex) || !Color.TryParse(artworkHex, out var color))
            return null;

        var hsl = color.ToHsl();
        if (hsl.S < GreyThreshold) return null;

        var s = Math.Max(hsl.S, MinSaturation);
        var l = Math.Clamp(hsl.L, MinLightness, MaxLightness);
        var rgb = new HslColor(1, hsl.H, s, l).ToRgb();
        return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }
}
