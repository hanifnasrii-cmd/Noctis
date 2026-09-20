using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// A ComboBox drop-down is a Popup whose routed events bubble through the box into the
/// page, so a wheel notch over an OPEN drop-down reaches the page ScrollViewer's smooth-
/// scroll tunnel handler. Before the guard, that handler scrolled the Settings page under
/// the popup (one notch = one Step) while the popup stayed put on screen, leaving the
/// Language list floating over the wrong card (user report 09-19, "click the system
/// language and it bugs out"). The wheel over a drop-down must never move the page.
/// </summary>
public class SmoothScrollPopupWheelTests
{
    [AvaloniaFact]
    public void WheelOverOpenDropDown_DoesNotScrollThePage()
    {
        ComboBoxDropDownAnimator.Install();
        var box = new ComboBox
        {
            ItemsSource = new[] { "System language", "English", "Espanol" }, SelectedIndex = 0,
            Width = 200, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        var content = new StackPanel();
        content.Children.Add(box);
        content.Children.Add(new Border { Height = 2000 });
        var scroller = new ScrollViewer { Content = content };
        var win = new Window { Width = 400, Height = 300, Content = scroller };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            SmoothScrollBehavior.SetIsEnabled(scroller, true);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "page must be scrollable");

            box.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var popup = box.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup");
            Assert.True(popup.IsOpen);
            var body = (Control)popup.Child!;
            var popupRoot = (Visual)body.GetVisualRoot()!;

            // A notch with the pointer over the drop-down list.
            var wheel = new PointerWheelEventArgs(
                body,
                new Pointer(0, PointerType.Mouse, true),
                popupRoot,
                new Point(10, 10),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None,
                new Vector(0, -1))
            { RoutedEvent = InputElement.PointerWheelChangedEvent };
            body.RaiseEvent(wheel);

            // (Handled is set inside the popup's own chain, so only the page offset tells.)
            Tick(TimeSpan.FromMilliseconds(500));
            Assert.Equal(0, scroller.Offset.Y);
            Assert.True(box.IsDropDownOpen);
        }
        finally
        {
            win.Close();
        }
    }

    /// <summary>The complement: a wheel on the page (pointer outside the list) scrolls the
    /// content out from under the popup, which on Windows stays put. The list closes as soon
    /// as the box moves, plainly (a fading ghost over the wrong card was the 09-19 glitch).</summary>
    [AvaloniaFact]
    public void WheelOverThePage_ClosesTheOpenDropDown()
    {
        ComboBoxDropDownAnimator.Install();
        var box = new ComboBox
        {
            ItemsSource = new[] { "System language", "English", "Espanol" }, SelectedIndex = 0,
            Width = 200, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        var content = new StackPanel();
        content.Children.Add(box);
        var filler = new Border { Height = 2000 };
        content.Children.Add(filler);
        var scroller = new ScrollViewer { Content = content };
        var win = new Window { Width = 400, Height = 300, Content = scroller };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            SmoothScrollBehavior.SetIsEnabled(scroller, true);

            box.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var popup = box.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup");
            Tick(TimeSpan.FromMilliseconds(400));
            Assert.True(popup.IsOpen);

            var wheel = new PointerWheelEventArgs(
                filler,
                new Pointer(0, PointerType.Mouse, true),
                win,
                new Point(10, 150),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None,
                new Vector(0, -1))
            { RoutedEvent = InputElement.PointerWheelChangedEvent };
            filler.RaiseEvent(wheel);

            Assert.True(wheel.Handled, "the page still scrolls");
            // The first glide frame moves the box; the popup must be gone on that same pass,
            // not 220ms of fade later.
            Tick(() => scroller.Offset.Y > 0, TimeSpan.FromSeconds(2));
            Assert.True(scroller.Offset.Y > 0, $"offset {scroller.Offset.Y} should have moved");
            Assert.False(box.IsDropDownOpen, "the list must close when the page scrolls under it");
            Assert.False(popup.IsOpen, "closed plainly on the frame the box moved, no fade");
        }
        finally
        {
            win.Close();
        }
    }

    /// <summary>Any page movement counts, not only the wheel: a scrollbar drag or a key sets
    /// Offset directly. And a fade already in flight (press outside, then drag) is cut short
    /// the moment the box moves.</summary>
    [AvaloniaFact]
    public void PageScrollsByOffset_ClosesTheOpenDropDown_EvenMidFade()
    {
        ComboBoxDropDownAnimator.Install();
        var box = new ComboBox
        {
            ItemsSource = new[] { "System language", "English", "Espanol" }, SelectedIndex = 0,
            Width = 200, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        var content = new StackPanel();
        content.Children.Add(box);
        content.Children.Add(new Border { Height = 2000 });
        var scroller = new ScrollViewer { Content = content };
        var win = new Window { Width = 400, Height = 300, Content = scroller };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();

            // Plain scroll: no press, no wheel.
            box.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var popup = box.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup");
            Tick(() => ((Control)popup.Child!).Opacity >= 1, TimeSpan.FromSeconds(2));
            var closed = 0;
            popup.Closed += (_, _) => closed++;
            scroller.Offset = new Vector(0, 120);
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsDropDownOpen);
            Assert.False(popup.IsOpen);
            Assert.Equal(1, closed);

            // Mid-fade: a press outside starts the fade, then the page moves under it.
            scroller.Offset = default;
            Dispatcher.UIThread.RunJobs();
            box.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Tick(() => ((Control)popup.Child!).Opacity >= 1, TimeSpan.FromSeconds(2));
            win.MouseDown(new Point(350, 250), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsDropDownOpen, "held open for the fade");
            scroller.Offset = new Vector(0, 120);
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsDropDownOpen);
            Assert.False(popup.IsOpen);
            Assert.Equal(2, closed);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void WheelOverThePage_StillScrolls()
    {
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 2000 });
        var scroller = new ScrollViewer { Content = content };
        var win = new Window { Width = 400, Height = 300, Content = scroller };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            SmoothScrollBehavior.SetIsEnabled(scroller, true);

            var wheel = new PointerWheelEventArgs(
                content,
                new Pointer(0, PointerType.Mouse, true),
                win,
                new Point(10, 10),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None,
                new Vector(0, -1))
            { RoutedEvent = InputElement.PointerWheelChangedEvent };
            content.RaiseEvent(wheel);

            Assert.True(wheel.Handled);
            Tick(TimeSpan.FromMilliseconds(500));
            Assert.True(scroller.Offset.Y > 0, $"offset {scroller.Offset.Y} should have moved");
        }
        finally
        {
            win.Close();
        }
    }

    private static void Tick(TimeSpan span) => Tick(() => false, span);

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
