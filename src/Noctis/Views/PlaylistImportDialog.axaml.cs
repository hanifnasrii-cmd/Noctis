using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Noctis.ViewModels;

namespace Noctis.Views;

public partial class PlaylistImportDialog : Window
{
    public PlaylistImportDialog()
    {
        InitializeComponent();
    }

    private bool _closing;

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

    public PlaylistImportDialog(PlaylistImportViewModel vm) : this()
    {
        DataContext = vm;
        vm.Closed += (_, _) => _ = CloseAnimatedAsync();
        ChooseFileButton.Click += OnChooseFile;

        // Drop an export file anywhere on the dialog instead of hunting for it in the picker.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // If a playlist link is already on the clipboard, offer it (Deezer imports at once,
        // other services get the "export it like this" guidance).
        Opened += async (_, _) =>
        {
            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard is null) return;
                vm.OfferClipboardText(await clipboard.TryGetTextAsync());
            }
            catch
            {
                // Clipboard access can fail on some desktops; the dialog works without it.
            }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes the same way the header X does.
        if (e.Key == Key.Escape && DataContext is PlaylistImportViewModel vm)
        {
            e.Handled = true;
            vm.CloseCommand.Execute(null);
            return;
        }
        base.OnKeyDown(e);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        // async void: an escaped exception would crash the app.
        try
        {
            if (DataContext is not PlaylistImportViewModel vm) return;
            var file = (e.DataTransfer.TryGetFiles() ?? Enumerable.Empty<IStorageItem>()).OfType<IStorageFile>().FirstOrDefault();
            var path = file?.TryGetLocalPath();
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                await vm.LoadFileAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistImportDialog] Drop failed: {ex.Message}");
        }
    }

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        // async void: an escaped exception would crash the app.
        try
        {
            if (DataContext is not PlaylistImportViewModel vm) return;

            var topLevel = GetTopLevel(this);
            if (topLevel is null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a playlist export",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Playlist exports") { Patterns = new[] { "*.csv", "*.json", "*.m3u", "*.m3u8" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                await vm.LoadFileAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistImportDialog] File pick failed: {ex.Message}");
        }
    }
}
