using Avalonia.Controls;
using Noctis.Mobile.ViewModels;
using Noctis.Models;

namespace Noctis.Mobile.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    // A tap plays the row; selection is cleared so the same row can be tapped again.
    private void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not Track track) return;
        (DataContext as ShellViewModel)?.PlaySongCommand.Execute(track);
        list.SelectedItem = null;
    }
}
