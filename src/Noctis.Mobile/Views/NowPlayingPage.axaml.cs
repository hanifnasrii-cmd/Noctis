using System;
using Avalonia.Controls;
using Avalonia.Input;
using Noctis.Mobile.ViewModels;

namespace Noctis.Mobile.Views;

public partial class NowPlayingPage : UserControl
{
    public NowPlayingPage()
    {
        InitializeComponent();
    }

    // Seek on release only: seeking per pixel during a drag stutters the decoder.
    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider && DataContext is ShellViewModel vm)
            vm.Player.Seek(TimeSpan.FromSeconds(slider.Value));
    }
}
