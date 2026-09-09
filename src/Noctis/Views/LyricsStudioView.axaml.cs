using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Noctis.Helpers;
using Noctis.Services;
using Noctis.ViewModels;

namespace Noctis.Views;

/// <summary>Sidebar Lyrics Studio page; the surface itself is <see cref="LyricsStudioPanel"/>.</summary>
public partial class LyricsStudioView : UserControl
{
    public LyricsStudioView()
    {
        InitializeComponent();
    }

    /// <summary>Opens the Lyrics Background Video picker (the same dialog Settings used to host).</summary>
    private async void OnModifyLyricsBackgroundClick(object? sender, RoutedEventArgs e)
    {
        // async void: an escaped exception would crash the app.
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            if (owner.DataContext is not MainWindowViewModel main) return;
            var library = App.Services?.GetService<ILibraryService>();
            if (library == null) return;

            var dialog = new LyricsBackgroundPickerDialog
            {
                DataContext = new LyricsBackgroundPickerViewModel(main.Settings, library)
            };
            DialogHelper.SizeToOwner(dialog, owner);
            await dialog.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsStudioView] Lyrics background picker failed: {ex.Message}");
        }
    }
}
