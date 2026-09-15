using Avalonia.Media;
using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>"Accent follows album art": the cover colour must land in a legible accent range.</summary>
public class ArtworkAccentTests
{
    [Fact]
    public void DarkNavy_IsLiftedIntoAccentLightness()
    {
        var hex = ArtworkAccent.TameForAccent("#1A1A2E");
        Assert.NotNull(hex);
        var hsl = Color.Parse(hex!).ToHsl();
        Assert.InRange(hsl.L, 0.42, 0.6);
        Assert.True(hsl.S >= 0.45, $"saturation {hsl.S}");
        Assert.InRange(hsl.H, 220, 260); // hue kept (blue)
    }

    [Fact]
    public void SaturatedRed_KeepsHueAndStaysInRange()
    {
        var hsl = Color.Parse(ArtworkAccent.TameForAccent("#E74856")!).ToHsl();
        Assert.InRange(hsl.H, 350, 360);
        Assert.InRange(hsl.L, 0.42, 0.6);
    }

    [Fact]
    public void GreyCover_ReturnsNull_SoUserAccentStays()
    {
        Assert.Null(ArtworkAccent.TameForAccent("#808080"));
        Assert.Null(ArtworkAccent.TameForAccent("#F2F2F2"));
        Assert.Null(ArtworkAccent.TameForAccent(null));
        Assert.Null(ArtworkAccent.TameForAccent("nope"));
    }
}
