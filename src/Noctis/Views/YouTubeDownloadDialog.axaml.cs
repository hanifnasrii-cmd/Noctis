using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Noctis.ViewModels;

namespace Noctis.Views;

public partial class YouTubeDownloadDialog : Window
{
    public YouTubeDownloadDialog()
    {
        InitializeComponent();
    }

    private bool _closing;

    public YouTubeDownloadDialog(YouTubeDownloadViewModel vm) : this()
    {
        DataContext = vm;
        vm.Closed += (_, _) => _ = CloseAnimatedAsync();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        QueryBox.Focus();
    }

    /// <summary>Settles the scrim and card to their open state on the frame after the
    /// window appears, so the transitions declared in XAML have something to animate to.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Dispatcher.UIThread.Post(() =>
        {
            DialogOverlay.Opacity = 1;
            DialogCard.RenderTransform = TransformOperations.Parse("scale(1)");
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Plays the fade/scale close animation, then closes the window.</summary>
    private async Task CloseAnimatedAsync()
    {
        if (_closing) return;
        _closing = true;
        DialogOverlay.Opacity = 0;
        DialogCard.RenderTransform = TransformOperations.Parse("scale(0.96)");
        await Task.Delay(200);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes the same way the header X does.
        if (e.Key == Key.Escape && DataContext is YouTubeDownloadViewModel vm)
        {
            e.Handled = true;
            vm.CloseCommand.Execute(null);
            return;
        }
        base.OnKeyDown(e);
    }
}
