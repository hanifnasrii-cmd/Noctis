using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctis.Models;
using Noctis.Services;
using Noctis.Services.LyricsStudio;

namespace Noctis.ViewModels;

/// <summary>What the picker hands back: the songs, in pick order, and the format to write.</summary>
public sealed record LyricsStudioPick(IReadOnlyList<Track> Tracks, bool WordTimings);

/// <summary>One search result: a song, or an album standing for every local song it holds.</summary>
public sealed partial class LyricsStudioPickRow : ObservableObject
{
    public required bool IsAlbum { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public string? ArtworkPath { get; init; }
    /// <summary>The local songs this row stands for (one for a song row).</summary>
    public required IReadOnlyList<Track> Tracks { get; init; }

    [ObservableProperty] private bool _isSelected;
    /// <summary>Song rows: what the song has now (word-level, line-level, plain only, no lyrics). Album rows: the song count.</summary>
    [ObservableProperty] private string _stateText = string.Empty;
}

/// <summary>
/// Lyrics Studio › Choose songs (user ask 09-19): search the library for songs and albums,
/// tick the ones to time, and pick the format (word timings = ELRC, line timings = LRC).
/// The page then runs the Studio over exactly those songs instead of its own "first 40
/// that lack the format" pick.
/// </summary>
public partial class LyricsStudioPickerViewModel : ObservableObject
{
    private const int MaxAlbumRows = 20;
    private const int MaxTrackRows = 80;

    private readonly ILibraryService _library;
    private readonly Func<IReadOnlyList<Track>, IReadOnlyList<LyricsFormat>> _detectFormats;
    private readonly HashSet<Guid> _selectedIds = new();
    private readonly List<Track> _picked = new();
    private int _scanGeneration;

    public ObservableCollection<LyricsStudioPickRow> Results { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _wordTimings;
    [ObservableProperty] private int _selectedCount;

    public bool LineTimings
    {
        get => !WordTimings;
        set => WordTimings = !value;
    }

    public bool ShowPrompt => string.IsNullOrWhiteSpace(SearchText);
    public bool ShowNoResults => !string.IsNullOrWhiteSpace(SearchText) && Results.Count == 0;
    public bool HasSelection => SelectedCount > 0;
    public string SelectionText => SelectedCount == 1 ? "1 song selected" : $"{SelectedCount} songs selected";
    public string AddButtonText => SelectedCount == 0
        ? Localization.Loc.T("LyricsStudioPicker.Add")
        : $"{Localization.Loc.T("LyricsStudioPicker.Add")} ({SelectedCount})";

    /// <summary>The last format scan kicked off by a search (tests await it).</summary>
    internal Task FormatScan { get; private set; } = Task.CompletedTask;

    public event EventHandler<LyricsStudioPick>? Confirmed;
    public event EventHandler? CloseRequested;

    public LyricsStudioPickerViewModel(ILibraryService library, bool wordTimings,
        Func<IReadOnlyList<Track>, IReadOnlyList<LyricsFormat>>? detectFormats = null)
    {
        _library = library;
        _wordTimings = wordTimings;
        _detectFormats = detectFormats ?? (tracks => ExistingLyricsLoader.DetectFormats(tracks));
    }

    partial void OnSearchTextChanged(string value) => RefreshResults();
    partial void OnWordTimingsChanged(bool value) => OnPropertyChanged(nameof(LineTimings));
    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(AddButtonText));
    }

    private void RefreshResults()
    {
        Results.Clear();
        var query = (SearchText ?? string.Empty).Trim();
        if (query.Length > 0)
        {
            foreach (var album in _library.Albums
                         .Where(a => Noctis.Helpers.SearchText.Matches(a.Name, query) || Noctis.Helpers.SearchText.Matches(a.Artist, query))
                         .Take(MaxAlbumRows))
            {
                var local = (album.Tracks ?? new List<Track>()).Where(t => t.SourceType == SourceType.Local).ToList();
                if (local.Count == 0) continue;
                Results.Add(new LyricsStudioPickRow
                {
                    IsAlbum = true,
                    Title = album.Name,
                    Subtitle = album.Artist,
                    ArtworkPath = album.ArtworkPath,
                    Tracks = local,
                    StateText = local.Count == 1 ? "1 song" : $"{local.Count} songs",
                    IsSelected = local.All(t => _selectedIds.Contains(t.Id)),
                });
            }
            foreach (var track in _library.Tracks
                         .Where(t => t.SourceType == SourceType.Local && PlaylistViewModel.MatchesSearch(t, query))
                         .Take(MaxTrackRows))
            {
                Results.Add(new LyricsStudioPickRow
                {
                    IsAlbum = false,
                    Title = track.TitleDisplay,
                    Subtitle = string.IsNullOrEmpty(track.Album) ? track.ArtistDisplay : $"{track.ArtistDisplay} · {track.Album}",
                    ArtworkPath = track.AlbumArtworkPath,
                    Tracks = new[] { track },
                    IsSelected = _selectedIds.Contains(track.Id),
                });
            }
            ScanFormats();
        }
        OnPropertyChanged(nameof(ShowPrompt));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    /// <summary>Format detection reads sidecars from disk, so it runs off the UI thread and
    /// fills the song rows in when it lands; a newer search discards an older scan.</summary>
    private void ScanFormats()
    {
        var rows = Results.Where(r => !r.IsAlbum).ToList();
        if (rows.Count == 0) return;
        var generation = ++_scanGeneration;
        var tracks = rows.Select(r => r.Tracks[0]).ToList();
        FormatScan = Task.Run(() => _detectFormats(tracks)).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || generation != _scanGeneration) return;
            var formats = t.Result;
            void Apply()
            {
                if (generation != _scanGeneration) return;
                for (var i = 0; i < rows.Count && i < formats.Count; i++)
                    rows[i].StateText = StateLabel(formats[i]);
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
        }, TaskScheduler.Default);
    }

    /// <summary>Same wording as the Studio's own song list pills.</summary>
    internal static string StateLabel(LyricsFormat format) => format switch
    {
        LyricsFormat.Elrc => "word-level",
        LyricsFormat.Lrc => "line-level",
        LyricsFormat.Plain => "plain only",
        _ => "no lyrics",
    };

    [RelayCommand]
    private void ToggleSelect(LyricsStudioPickRow? row)
    {
        if (row is null) return;
        var allIn = row.Tracks.All(t => _selectedIds.Contains(t.Id));
        if (allIn)
        {
            foreach (var t in row.Tracks) _selectedIds.Remove(t.Id);
            _picked.RemoveAll(t => row.Tracks.Any(r => r.Id == t.Id));
        }
        else
        {
            foreach (var t in row.Tracks)
                if (_selectedIds.Add(t.Id)) _picked.Add(t);
        }
        foreach (var r in Results)
            r.IsSelected = r.Tracks.All(t => _selectedIds.Contains(t.Id));
        SelectedCount = _selectedIds.Count;
    }

    /// <summary>The songs ticked so far, in the order they were ticked.</summary>
    public IReadOnlyList<Track> PickedTracks => _picked;

    [RelayCommand]
    private void Confirm()
    {
        if (_picked.Count == 0) return;
        Confirmed?.Invoke(this, new LyricsStudioPick(_picked.ToList(), WordTimings));
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
