using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctis.Mobile.ViewModels;
using Noctis.Models;

namespace Noctis.Mobile.Views;

public partial class QueuePage : UserControl
{
    public QueuePage()
    {
        InitializeComponent();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Track track } || DataContext is not ShellViewModel vm) return;
        var index = vm.Player.UpNext.IndexOf(track);
        if (index >= 0) vm.Player.RemoveFromQueue(index);
    }
}
