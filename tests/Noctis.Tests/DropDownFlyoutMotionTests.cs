using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The Lyrics Studio Options flyout plays the ComboBox drop-down's motion (fade + glide,
/// held open for the fade-out), and the Model pill's list opens above the pill.
/// </summary>
public class DropDownFlyoutMotionTests
{
    [AvaloniaFact]
    public void Flyout_OpensWithDropDownMotion_AndHoldsCloseForFadeOut()
    {
        var content = new Border { Width = 160, Height = 80 };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.Top };
        DropDownFlyoutMotion.SetEnable(flyout, true);
        var button = new Button
        {
            Content = "Options", Flyout = flyout,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        var win = new Window { Width = 400, Height = 300, Content = button };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            var closed = 0;
            flyout.Closed += (_, _) => closed++;

            flyout.ShowAt(button);
            Assert.True(flyout.IsOpen);
            var body = content.FindAncestorOfType<FlyoutPresenter>();
            Assert.NotNull(body);
            // Start state written synchronously: transparent, shrunk, anchored at the bottom
            // edge because the flyout opens above its button.
            Assert.Equal(0, body!.Opacity);
            Assert.Equal(0.94, Scale(body), 3);
            Assert.Equal(1, body.RenderTransformOrigin.Point.Y, 3);
            Assert.NotNull(body.Transitions);

            Dispatcher.UIThread.RunJobs();
            Tick(() => body.Opacity > 0 && body.Opacity < 1, TimeSpan.FromSeconds(2));
            Assert.True(body.Opacity > 0 && body.Opacity < 1, $"opacity {body.Opacity} should be mid fade-in");
            Tick(() => body.Opacity >= 1 && Math.Abs(Scale(body) - 1) < 1e-3, TimeSpan.FromSeconds(2));
            Assert.Equal(1, body.Opacity);
            Assert.Equal(1, Scale(body), 3);

            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen, "close should be held open while the flyout fades");
            Assert.Equal(0, closed);
            Tick(() => body.Opacity < 1, TimeSpan.FromSeconds(2));
            Assert.True(body.Opacity > 0 && body.Opacity < 1, $"opacity {body.Opacity} should be mid fade-out");
            Tick(() => !flyout.IsOpen, TimeSpan.FromSeconds(2));
            Assert.False(flyout.IsOpen);
            Assert.Equal(1, closed);

            // Reopens play the entrance again.
            flyout.ShowAt(button);
            var body2 = content.FindAncestorOfType<FlyoutPresenter>()!;
            Assert.Equal(0, body2.Opacity);
            Tick(() => body2.Opacity >= 1, TimeSpan.FromSeconds(2));
            Assert.Equal(1, body2.Opacity);
        }
        finally
        {
            win.Close();
        }
    }

    /// <summary>The Fluent template leaves Popup.Placement at its default, so a style on the
    /// template part can point the list upward (LyricsStudioPanel "ComboBox.open-up").</summary>
    [AvaloniaFact]
    public void OpenUpClass_PlacesThePopupAboveTheBox()
    {
        var box = new ComboBox
        {
            ItemsSource = new[] { "Base", "Medium" }, SelectedIndex = 1, Width = 150,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        box.Classes.Add("open-up");
        var win = new Window { Width = 400, Height = 300, Content = box };
        var style = new Style(x => x.OfType<ComboBox>().Class("open-up").Template().OfType<Popup>().Name("PART_Popup"));
        style.Setters.Add(new Setter(Popup.PlacementProperty, PlacementMode.TopEdgeAlignedLeft));
        win.Styles.Add(style);
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            var popup = box.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup");
            Assert.Equal(PlacementMode.TopEdgeAlignedLeft, popup.Placement);
        }
        finally
        {
            win.Close();
        }
    }

    private static double Scale(Control c) =>
        ((Avalonia.Media.Transformation.TransformOperations)c.RenderTransform!).Value.M11;

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
