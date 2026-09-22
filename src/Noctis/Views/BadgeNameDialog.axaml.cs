using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Noctis.Views;

/// <summary>GitHub #74: asks for a badge name. <see cref="Result"/> is null on cancel.</summary>
public partial class BadgeNameDialog : Window
{
    private bool _closing;

    public string? Result { get; private set; }

    public BadgeNameDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Dispatcher.UIThread.Post(() =>
        {
            DialogOverlay.Opacity = 1;
            DialogCard.RenderTransform = TransformOperations.Parse("scale(1)");
            NameTextBox.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _ = CloseAnimatedAsync();

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Accept(); }
        else if (e.Key == Key.Escape) { e.Handled = true; _ = CloseAnimatedAsync(); }
    }

    private void Accept()
    {
        var name = NameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { NameTextBox.Focus(); return; }
        Result = name;
        _ = CloseAnimatedAsync();
    }

    private async Task CloseAnimatedAsync()
    {
        if (_closing) return;
        _closing = true;
        DialogOverlay.Opacity = 0;
        DialogCard.RenderTransform = TransformOperations.Parse("scale(0.96)");
        await Task.Delay(200);
        Close();
    }

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnOverlayWheel(object? sender, PointerWheelEventArgs e) => e.Handled = true;
}
