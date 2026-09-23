using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctis.Mobile.ViewModels;

namespace Noctis.Mobile.Views;

public partial class NowPlayingPage : UserControl
{
    public NowPlayingPage()
    {
        InitializeComponent();

        // Tunnel routing, and registered here rather than as XAML event attributes, for two
        // reasons. First, the same one the desktop PlaybackBarView documents: these must fire
        // BEFORE the Slider's internal Thumb/Track handlers. Second, a XAML event attribute
        // registers a Bubble handler with handledEventsToo:false, so a Thumb that marks the
        // release handled at the end of a drag would silently skip the commit entirely.
        SeekBar.AddHandler(InputElement.PointerPressedEvent, OnSeekPressed, RoutingStrategies.Tunnel);
        SeekBar.AddHandler(InputElement.PointerReleasedEvent, OnSeekReleased, RoutingStrategies.Tunnel);
        // Feeds the ViewModel's seek watchdog. Tunnel again, and for the same reason: during a
        // drag Avalonia's Thumb holds the pointer capture, so the moves are routed to the
        // Thumb's route — this Slider is on it as an ancestor, but only a tunnel/bubble
        // handler sees them, and a Thumb that marks a move handled would hide it from bubble.
        SeekBar.AddHandler(InputElement.PointerMovedEvent, OnSeekMoved, RoutingStrategies.Tunnel);
        // A gesture cancelled by something stealing the pointer (a notification, a system
        // gesture) never reaches the release handler; without this the ViewModel would stay
        // in "seeking" and the thumb would sit frozen for the rest of the track. Not
        // sufficient on its own: PointerCaptureLost is a Direct event raised on whatever held
        // the capture (the Thumb, not this Slider — the press handler deliberately does not
        // capture, so the Slider's own drag logic still runs), and the shade-cancel case
        // observed on device delivers no pointer event at all. The watchdog is the backstop.
        SeekBar.PointerCaptureLost += OnSeekCaptureLost;
    }

    // Hold the position push for the duration of the drag: the player ticks 4x a second and
    // each tick would otherwise overwrite the value the drag is putting into the Slider.
    private void OnSeekPressed(object? sender, PointerPressedEventArgs e)
        => (DataContext as ShellViewModel)?.Player.BeginSeek();

    // Proof the finger is still on the bar, so the watchdog does not cut a slow drag short.
    private void OnSeekMoved(object? sender, PointerEventArgs e)
        => (DataContext as ShellViewModel)?.Player.KeepSeekAlive();

    // Seek on release only: seeking per pixel during a drag stutters the decoder.
    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not ShellViewModel vm) return;
        vm.Player.EndSeek();
        vm.Player.Seek(TimeSpan.FromSeconds(slider.Value));
    }

    private void OnSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => (DataContext as ShellViewModel)?.Player.EndSeek();
}
