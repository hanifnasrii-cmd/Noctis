using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctis.Helpers;
using Noctis.Models;
using Noctis.Services;

namespace Noctis.ViewModels;

/// <summary>One row of the lyrics background picker: a song or an album.</summary>
public partial class LyricsBackgroundPickItem : ObservableObject
{
    public required string Key { get; init; }
    public required bool IsAlbum { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public string? ArtworkPath { get; init; }

    /// <summary>File name of the item's own clip, or empty when it uses the default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnVideo))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _videoName = string.Empty;

    public bool HasOwnVideo => !string.IsNullOrEmpty(VideoName);
    public string KindLabel => IsAlbum ? Localization.Loc.T("LyricsBackground.Album") : Localization.Loc.T("LyricsBackground.Song");
    public string StatusText => HasOwnVideo ? VideoName : Localization.Loc.T("LyricsBackground.UsesDefault");
}

/// <summary>
/// Settings › Appearance › Lyrics Background Video › Modify (user ask 2026-09-07). One place
/// for the default clip and for the songs and albums that carry their own: search the
/// library, give any row a video, or send it back to the default. Writes go straight
/// through <see cref="SettingsViewModel"/> so the lyrics page follows immediately.
/// </summary>
public partial class LyricsBackgroundPickerViewModel : ObservableObject
{
    private const int MaxAlbumRows = 20;
    private const int MaxTrackRows = 80;

    private readonly SettingsViewModel _settings;
    private readonly ILibraryService _library;

    /// <summary>Opens the video file picker; set by the dialog so the picker parents to it.</summary>
    public Func<Task<string?>>? PickFile { get; set; }

    public event EventHandler? CloseRequested;

    public ObservableCollection<LyricsBackgroundPickItem> Results { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDefaultVideo))]
    private string _defaultVideoName = string.Empty;

    public bool HasDefaultVideo => !string.IsNullOrEmpty(DefaultVideoName);

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);
    /// <summary>No search: the list shows the songs and albums that already have their own clip.</summary>
    public bool ShowOverridesHeader => !IsSearching && Results.Count > 0;
    public bool ShowPrompt => !IsSearching && Results.Count == 0;
    public bool ShowNoResults => IsSearching && Results.Count == 0;

    public LyricsBackgroundPickerViewModel(SettingsViewModel settings, ILibraryService library)
    {
        _settings = settings;
        _library = library;
        DefaultVideoName = settings.HasLyricsBackgroundMedia ? settings.LyricsBackgroundMediaName : string.Empty;
        RefreshResults();
    }

    partial void OnSearchTextChanged(string value) => RefreshResults();

    private void RefreshResults()
    {
        Results.Clear();
        var query = (SearchText ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            foreach (var item in OverrideRows()) Results.Add(item);
        }
        else
        {
            foreach (var album in _library.Albums
                         .Where(a => Noctis.Helpers.SearchText.Matches(a.Name, query) || Noctis.Helpers.SearchText.Matches(a.Artist, query))
                         .Take(MaxAlbumRows))
                Results.Add(RowForAlbum(album));
            foreach (var track in _library.Tracks
                         .Where(t => PlaylistViewModel.MatchesSearch(t, query))
                         .Take(MaxTrackRows))
                Results.Add(RowForTrack(track));
        }
        RaiseStateProperties();
    }

    private IEnumerable<LyricsBackgroundPickItem> OverrideRows()
    {
        var albums = _library.Albums;
        var tracks = _library.Tracks;
        foreach (var key in _settings.LyricsBackgroundOverrideKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (key.StartsWith("album:", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(key.AsSpan(6), "N", out var albumId))
            {
                var album = albums.FirstOrDefault(a => a.Id == albumId);
                if (album != null) yield return RowForAlbum(album);
            }
            else if (key.StartsWith("track:", StringComparison.OrdinalIgnoreCase)
                     && Guid.TryParseExact(key.AsSpan(6), "N", out var trackId))
            {
                var track = tracks.FirstOrDefault(t => t.Id == trackId);
                if (track != null) yield return RowForTrack(track);
            }
        }
    }

    private LyricsBackgroundPickItem RowForAlbum(Album album)
    {
        var key = LyricsBackgroundOverrides.KeyForAlbum(album);
        return new LyricsBackgroundPickItem
        {
            Key = key,
            IsAlbum = true,
            Title = album.Name,
            Subtitle = album.Artist,
            ArtworkPath = album.ArtworkPath,
            VideoName = VideoNameFor(key),
        };
    }

    private LyricsBackgroundPickItem RowForTrack(Track track)
    {
        var key = LyricsBackgroundOverrides.KeyForTrack(track);
        return new LyricsBackgroundPickItem
        {
            Key = key,
            IsAlbum = false,
            Title = track.TitleDisplay,
            Subtitle = string.IsNullOrEmpty(track.Album) ? track.ArtistDisplay : $"{track.ArtistDisplay} · {track.Album}",
            ArtworkPath = track.AlbumArtworkPath,
            VideoName = VideoNameFor(key),
        };
    }

    private string VideoNameFor(string key)
    {
        var path = _settings.GetLyricsBackgroundOverridePath(key);
        return string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(ShowOverridesHeader));
        OnPropertyChanged(nameof(ShowPrompt));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    private async Task ChooseDefaultAsync()
    {
        var path = await PickAsync();
        if (path is null) return;
        await _settings.SetLyricsBackgroundMediaAsync(path);
        DefaultVideoName = _settings.HasLyricsBackgroundMedia ? _settings.LyricsBackgroundMediaName : string.Empty;
    }

    [RelayCommand]
    private void ClearDefault()
    {
        _settings.ClearLyricsBackgroundMediaCommand.Execute(null);
        DefaultVideoName = string.Empty;
    }

    [RelayCommand]
    private async Task ChooseForItemAsync(LyricsBackgroundPickItem? item)
    {
        if (item is null) return;
        var path = await PickAsync();
        if (path is null) return;
        await _settings.SetLyricsBackgroundOverrideAsync(item.Key, path);
        item.VideoName = VideoNameFor(item.Key);
        RaiseStateProperties();
    }

    [RelayCommand]
    private void ClearForItem(LyricsBackgroundPickItem? item)
    {
        if (item is null) return;
        _settings.ClearLyricsBackgroundOverride(item.Key);
        item.VideoName = string.Empty;
        // The overrides list (no search) drops the row; a search keeps it with "Uses default".
        if (!IsSearching) Results.Remove(item);
        RaiseStateProperties();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task<string?> PickAsync()
    {
        if (PickFile is null) return null;
        try
        {
            var path = await PickFile();
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricsBackground] pick failed: {ex.Message}");
            return null;
        }
    }
}
