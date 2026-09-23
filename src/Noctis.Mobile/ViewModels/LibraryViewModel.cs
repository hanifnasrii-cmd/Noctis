using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctis.Mobile.Services;
using Noctis.Models;
using Noctis.Services;

namespace Noctis.Mobile.ViewModels;

/// <summary>
/// The phone Library page over the shared Core library: the six count tiles, the flat
/// song list, and the folder flow (SAF pick → AppSettings.MusicFolders → scan). Library
/// events arrive on scan threads; <c>marshal</c> hops them to the UI thread (tests pass
/// a direct call).
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private const int RecentlyAddedDays = 30;

    private readonly ILibraryService _library;
    private readonly IPersistenceService _persistence;
    private readonly IFolderPicker _picker;
    private readonly Action<Action> _marshal;

    public LibraryViewModel(ILibraryService library, IPersistenceService persistence, IFolderPicker picker,
        Action<Action>? marshal = null)
    {
        _library = library;
        _persistence = persistence;
        _picker = picker;
        _marshal = marshal ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));

        _library.LibraryUpdated += (_, _) => _marshal(RefreshFromLibrary);
        _library.ScanProgress += (_, n) => _marshal(() => ScanProgress = n);
        _library.ScanAborted += (_, roots) => _marshal(() => StatusText = $"{roots.Length} folder(s) unavailable. Reconnect them and rescan.");
    }

    [ObservableProperty] private int _songCount;
    [ObservableProperty] private int _albumCount;
    [ObservableProperty] private int _artistCount;
    [ObservableProperty] private int _favoriteCount;
    [ObservableProperty] private int _recentlyAddedCount;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _scanProgress;
    [ObservableProperty] private bool _hasFolders;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Every track, sorted by title. Rebuilt on each library update.</summary>
    public ObservableCollection<Track> Songs { get; } = new();

    /// <summary>The configured roots (SAF tree URIs on Android), mirrored from settings.</summary>
    public ObservableCollection<string> Folders { get; } = new();

    public async Task InitializeAsync()
    {
        var settings = await _persistence.LoadSettingsAsync();
        SetFolders(settings.MusicFolders);
        RefreshFromLibrary();
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var picked = await _picker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(picked)) return;

        var settings = await _persistence.LoadSettingsAsync();
        if (!settings.MusicFolders.Contains(picked))
        {
            settings.MusicFolders.Add(picked);
            await _persistence.SaveSettingsAsync(settings);
        }
        SetFolders(settings.MusicFolders);
        await ScanAsync(settings.MusicFolders);
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        var settings = await _persistence.LoadSettingsAsync();
        if (settings.MusicFolders.Count == 0) return;
        await ScanAsync(settings.MusicFolders);
    }

    private async Task ScanAsync(IEnumerable<string> folders)
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanProgress = 0;
        StatusText = "Scanning…";
        try
        {
            await _library.ScanAsync(folders);
            if (StatusText == "Scanning…") StatusText = string.Empty; // ScanAborted may have replaced it
        }
        catch (Exception ex)
        {
            DebugLog.Write("Library", $"Phone scan failed: {ex.Message}");
            StatusText = "Scan failed. See the log.";
        }
        finally
        {
            IsScanning = false;
            RefreshFromLibrary();
        }
    }

    private void SetFolders(IEnumerable<string> folders)
    {
        Folders.Clear();
        foreach (var f in folders) Folders.Add(f);
        HasFolders = Folders.Count > 0;
    }

    private void RefreshFromLibrary()
    {
        var tracks = _library.Tracks;
        SongCount = tracks.Count;
        AlbumCount = _library.Albums.Count;
        ArtistCount = _library.Artists.Count;
        FavoriteCount = tracks.Count(t => t.IsFavorite);
        var cutoff = DateTime.UtcNow.AddDays(-RecentlyAddedDays);
        RecentlyAddedCount = tracks.Count(t => t.DateAdded >= cutoff);

        Songs.Clear();
        foreach (var t in tracks.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase))
            Songs.Add(t);
    }
}
