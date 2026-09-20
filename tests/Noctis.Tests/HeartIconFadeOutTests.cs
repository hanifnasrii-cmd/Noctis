using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Noctis.Controls;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Un-favoriting must fade the filled heart out the way favoriting fades it in.
/// The glyph has to stay visible while its opacity transition runs; hiding it on the
/// same tick as the toggle snaps it away with no animation.
/// </summary>
public class HeartIconFadeOutTests
{
    private static HeartIcon Mount(bool favorite)
    {
        var heart = new HeartIcon { IsFavorite = favorite, ShowWhenOff = false, Size = 14 };
        var window = new Window { Content = heart };
        window.Show();
        return heart;
    }

    [AvaloniaFact]
    public async Task Unfavorite_KeepsFilledGlyphVisibleWhileFadingOut()
    {
        var heart = Mount(favorite: true);
        // Let the rebind window pass so the toggle counts as a click, not a re-bind.
        await Task.Delay(200);
        Assert.NotNull(heart.VisibleGlyph);

        heart.IsFavorite = false;

        // Immediately after the toggle the filled glyph must still be in the tree
        // (fading), not hidden outright.
        Assert.NotNull(heart.VisibleGlyph);

        // Once the pop duration has elapsed it is hidden.
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(heart.VisibleGlyph);
    }

    [AvaloniaFact]
    public async Task Refavorite_DuringFadeOut_KeepsGlyph()
    {
        var heart = Mount(favorite: true);
        await Task.Delay(200);

        heart.IsFavorite = false;
        heart.IsFavorite = true;
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(heart.VisibleGlyph);
    }

    [AvaloniaFact]
    public void Rebind_SnapsWithoutFade()
    {
        var heart = Mount(favorite: true);

        // Within the rebind window a change is treated as a data-context swap: snap.
        heart.IsFavorite = false;

        Assert.Null(heart.VisibleGlyph);
    }
}
