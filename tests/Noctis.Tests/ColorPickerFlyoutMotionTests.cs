using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Controls;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The accent picker flyout plays the Settings card's open/close motion (140ms fade +
/// 0.96 scale). Pin that the open starts shrunk/transparent and eases to rest, and that a
/// close is held open for the fade-out before the flyout really hides.
/// </summary>
public class ColorPickerFlyoutMotionTests
{
    [AvaloniaFact]
    public void Open_FadesAndScalesIn_Close_FadesOutThenHides()
    {
        var picker = new ColorPickerFlyout
        {
            Hex = "#E74856",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var win = new Window { Width = 800, Height = 600, Content = picker };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var button = picker.FindControl<Button>("SwatchButton");
            Assert.NotNull(button);
            var flyout = Assert.IsType<Flyout>(button!.Flyout);
            var closed = 0;
            flyout.Closed += (_, _) => closed++;

            flyout.ShowAt(button);
            Assert.True(flyout.IsOpen);
            var body = picker.FindControl<Border>("PickerBody")!.FindAncestorOfType<FlyoutPresenter>();
            Assert.NotNull(body);
            // Start state is written synchronously, before the first frame.
            Assert.Equal(0, body!.Opacity);
            Assert.Equal(0.96, Scale(body), 3);
            Assert.NotNull(body.Transitions);

            Dispatcher.UIThread.RunJobs();
            Tick(() => body.Opacity > 0 && body.Opacity < 1, TimeSpan.FromSeconds(2));
            Assert.True(body.Opacity > 0 && body.Opacity < 1, $"opacity {body.Opacity} should be mid fade-in");
            Tick(() => body.Opacity >= 1 && Math.Abs(Scale(body) - 1) < 1e-3, TimeSpan.FromSeconds(2));
            Assert.Equal(1, body.Opacity);
            Assert.Equal(1, Scale(body), 3);

            // Hide is caught by Closing: cancelled, faded, then closed for real.
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen, "close should be held open while the picker fades");
            Assert.Equal(0, closed);
            Tick(() => body.Opacity < 1, TimeSpan.FromSeconds(2));
            Assert.True(body.Opacity > 0 && body.Opacity < 1, $"opacity {body.Opacity} should be mid fade-out");
            Tick(() => !flyout.IsOpen, TimeSpan.FromSeconds(2));
            Assert.False(flyout.IsOpen);
            Assert.Equal(1, closed);

            // A second open plays the entrance again from the shrunk state.
            flyout.ShowAt(button);
            var body2 = picker.FindControl<Border>("PickerBody")!.FindAncestorOfType<FlyoutPresenter>()!;
            Assert.Equal(0, body2.Opacity);
            Assert.Equal(0.96, Scale(body2), 3);
            Tick(() => body2.Opacity >= 1, TimeSpan.FromSeconds(2));
            Assert.Equal(1, body2.Opacity);
            // Light dismiss (a press on the window away from the picker) takes the same
            // held-open fade: the popup's own close routes through Flyout.Closing.
            win.MouseDown(new Point(700, 500), Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen, "light dismiss should be held open while the picker fades");
            Assert.Equal(1, closed);
            Tick(() => body2.Opacity < 1, TimeSpan.FromSeconds(2));
            Assert.True(body2.Opacity > 0 && body2.Opacity < 1, $"opacity {body2.Opacity} should be mid fade-out");
            Tick(() => !flyout.IsOpen, TimeSpan.FromSeconds(2));
            Assert.False(flyout.IsOpen);
            Assert.Equal(2, closed);
        }
        finally
        {
            win.Close();
        }
    }

    private static double Scale(Control c) =>
        ((Avalonia.Media.Transformation.TransformOperations)c.RenderTransform!).Value.M11;

    /// <summary>Headless has no free-running render timer: tick it by hand while real time passes.</summary>
    private static void Tick(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
