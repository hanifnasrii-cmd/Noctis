using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Noctis.Models;
using Noctis.Services;
using Noctis.Services.LyricsStudio;

namespace Noctis.ViewModels;

/// <summary>
/// The sidebar's Lyrics Studio page. Each visit scans the local library for the songs that
/// lack the format the Studio writes (ELRC with word timings on, any timed lyrics off), shows
/// the per-format counts, and opens a <see cref="LyricsStudioViewModel"/> over the first 40 —
/// unless a run or a review is in progress, in which case the page keeps its work.
/// </summary>
public partial class LyricsStudioPageViewModel : ViewModelBase
{
    /// <summary>First-N cap so a run stays reviewable (same cap as the old Settings button).</summary>
    public const int MaxQueue = 40;

    private readonly ILibraryService _library;
    private readonly Func<bool> _wordTimings;
    private readonly Func<IReadOnlyList<Track>, LyricsStudioViewModel> _createStudio;
    private int _generation;

    [ObservableProperty] private LyricsStudioViewModel? _studio;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    /// <summary>ELRC / LRC / plain / none counts over the local library (empty until the scan finishes).</summary>
    [ObservableProperty] private IReadOnlyList<LyricsStudioCount> _counts = Array.Empty<LyricsStudioCount>();

    /// <summary>
    /// Whether the two backdrop cards (lyrics background video, music videos) are folded out.
    /// They are settings, not work, so the page opens straight onto the queue and review and
    /// keeps them one click away behind the header's Backdrops pill.
    /// </summary>
    [ObservableProperty] private bool _showBackdrops;

    public bool ShowEmpty => !IsLoading && Studio is null;
    public bool HasCounts => Counts.Count > 0;

    public LyricsStudioPageViewModel(ILibraryService library, Func<bool> wordTimings,
        Func<IReadOnlyList<Track>, LyricsStudioViewModel> createStudio)
    {
        _library = library;
        _wordTimings = wordTimings;
        _createStudio = createStudio;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));
    partial void OnStudioChanged(LyricsStudioViewModel? value) => OnPropertyChanged(nameof(ShowEmpty));
    partial void OnCountsChanged(IReadOnlyList<LyricsStudioCount> value) => OnPropertyChanged(nameof(HasCounts));

    /// <summary>True when the open Studio must not be replaced by a rescan.</summary>
    public bool IsBusy => Studio is { IsRunning: true } or { HasReview: true };

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        var generation = ++_generation;
        IsLoading = true;
        StatusText = "Counting lyrics across the library…";
        try
        {
            var wordTimings = _wordTimings();
            var local = _library.Tracks.Where(t => t.SourceType == SourceType.Local).ToList();
            // Format detection reads disk per track — keep the library-wide pass off the UI thread.
            var formats = await Task.Run(() => ExistingLyricsLoader.DetectFormats(local));
            if (generation != _generation) return;

            Counts = SettingsViewModel.BuildLyricsStudioCounts(formats);
            var picked = PickMissing(local, formats, wordTimings, MaxQueue);
            Studio = picked.Count > 0 ? _createStudio(picked) : null;
            StatusText = picked.Count == 0
                ? "Every local song already has the chosen format."
                : string.Empty;
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            Studio = null;
            StatusText = $"Could not scan the library — {ex.Message}";
        }
        finally
        {
            if (generation == _generation) IsLoading = false;
        }
    }

    /// <summary>The first <paramref name="max"/> tracks whose lyrics lack the chosen format.</summary>
    internal static List<Track> PickMissing(IReadOnlyList<Track> tracks, IReadOnlyList<LyricsFormat> formats, bool wordTimings, int max)
    {
        var picked = new List<Track>(Math.Min(max, tracks.Count));
        for (var i = 0; i < tracks.Count && i < formats.Count && picked.Count < max; i++)
            if (!LyricsFormatDetector.AlreadyHas(formats[i], wordTimings)) picked.Add(tracks[i]);
        return picked;
    }
}
