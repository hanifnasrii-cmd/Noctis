using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctis.Models;
using Noctis.Services;

namespace Noctis.Mobile.ViewModels;

/// <summary>
/// The phone transport: Core <see cref="PlaybackQueue"/> for order/repeat/shuffle/history,
/// <see cref="IAudioPlayer"/> for sound. The next queued file is handed to
/// <see cref="IAudioPlayer.PrepareNext"/> so the Media3 player can chain it gaplessly; on
/// that automatic transition the player raises TrackEnded and our Play(next) is a no-op
/// restart (the player recognises the path). Queue and position are saved every
/// <see cref="SaveIntervalSeconds"/> and on pause, and restored paused on launch, so
/// process death costs at most five seconds. Player events may arrive on any thread;
/// <c>marshal</c> hops them to the UI thread (tests pass a direct call).
/// </summary>
public sealed partial class NowPlayingViewModel : ObservableObject, IDisposable
{
    private const int SaveIntervalSeconds = 5;
    // Circuit breaker for the error→skip loop (a folder of dead URIs after a revoked grant).
    private const int MaxConsecutiveErrors = 5;

    private readonly IAudioPlayer _player;
    private readonly ILibraryService _library;
    private readonly IPersistenceService _persistence;
    private readonly IPlayHistoryService? _history;
    private readonly Action<Action> _marshal;

    private PlaybackQueue _queue = new();
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private bool _gapless = true;
    private int _consecutiveErrors;

    public NowPlayingViewModel(IAudioPlayer player, ILibraryService library, IPersistenceService persistence,
        IPlayHistoryService? history = null, Action<Action>? marshal = null)
    {
        _player = player;
        _library = library;
        _persistence = persistence;
        _history = history;
        _marshal = marshal ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));

        _player.PositionChanged += OnPlayerPosition;
        _player.DurationResolved += OnPlayerDuration;
        _player.TrackEnded += OnPlayerTrackEnded;
        _player.PlaybackError += OnPlayerError;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    private Track? _currentTrack;

    [ObservableProperty] private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private TimeSpan _duration;

    [ObservableProperty] private RepeatMode _repeatMode;
    [ObservableProperty] private bool _isShuffleEnabled;
    [ObservableProperty] private string _errorText = string.Empty;

    /// <summary>Mirror of the queue's UpNext for the Queue page.</summary>
    public ObservableCollection<Track> UpNext { get; } = new();

    public bool HasTrack => CurrentTrack != null;

    /// <summary>0..1 for the seek bar.</summary>
    public double ProgressFraction => Duration > TimeSpan.Zero ? Math.Clamp(Position / Duration, 0, 1) : 0;

    public void SetGapless(bool enabled)
    {
        _gapless = enabled;
        _player.SetGapless(enabled);
        PrepareUpcoming();
    }

    /// <summary>Replace the queue with <paramref name="tracks"/> and start at <paramref name="startIndex"/>.</summary>
    public void PlayTracks(IReadOnlyList<Track> tracks, int startIndex)
    {
        var first = _queue.ReplaceAll(tracks, startIndex);
        IsShuffleEnabled = _queue.IsShuffleEnabled;
        if (first == null) return;
        StartTrack(first, fromPosition: null);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (CurrentTrack == null) return;
        switch (_player.State)
        {
            case PlaybackState.Playing:
                _player.Pause();
                IsPlaying = false;
                SaveStateNow();
                break;
            case PlaybackState.Paused:
                _player.Resume();
                IsPlaying = true;
                break;
            default:
                // Stopped: a queue restored after process death (or a finished queue) — start
                // the loaded track from the position we are showing.
                StartTrack(CurrentTrack, fromPosition: Position);
                break;
        }
    }

    [RelayCommand]
    private void Next()
    {
        var next = _queue.Advance(QueueAdvance.UserSkip);
        if (next == null) StopPlayback(); else StartTrack(next, null);
    }

    [RelayCommand]
    private void Previous()
    {
        if (CurrentTrack != null && Position > TimeSpan.FromSeconds(3))
        {
            Seek(TimeSpan.Zero);
            return;
        }
        var previous = _queue.Back();
        if (previous != null) StartTrack(previous, null);
    }

    public void Seek(TimeSpan position)
    {
        if (CurrentTrack == null) return;
        Position = position;
        if (_player.State != PlaybackState.Stopped)
            _player.Seek(position);
    }

    [RelayCommand]
    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        _queue.RepeatMode = RepeatMode;
        PrepareUpcoming();
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _queue.SetShuffle(!_queue.IsShuffleEnabled);
        IsShuffleEnabled = _queue.IsShuffleEnabled;
        QueueChanged();
    }

    public void PlayNext(Track track) { _queue.AddNext(track); QueueChanged(); }
    public void AddToQueue(Track track) { _queue.Add(track); QueueChanged(); }
    public void RemoveFromQueue(int upNextIndex) { _queue.RemoveAt(upNextIndex); QueueChanged(); }
    public void MoveInQueue(int fromUpNextIndex, int toUpNextIndex) { _queue.Move(fromUpNextIndex, toUpNextIndex); QueueChanged(); }

    private void QueueChanged()
    {
        SyncUpNext();
        PrepareUpcoming();
        SaveStateNow();
    }

    private void StartTrack(Track track, TimeSpan? fromPosition)
    {
        CurrentTrack = track;
        Position = fromPosition ?? TimeSpan.Zero;
        Duration = track.Duration;
        ErrorText = string.Empty;
        _player.PendingSeekMs = fromPosition is { } p && p > TimeSpan.Zero ? (long)p.TotalMilliseconds : -1;
        _player.Play(track.FilePath);
        IsPlaying = true;
        _history?.RecordPlay(track);
        SyncUpNext();
        PrepareUpcoming();
        SaveStateNow();
    }

    private void PrepareUpcoming()
    {
        if (!_gapless || _queue.UpNext.Count == 0 || CurrentTrack == null)
        {
            _player.CancelPreparedNext();
            return;
        }
        _player.PrepareNext(_queue.UpNext[0].FilePath);
    }

    private void StopPlayback()
    {
        _player.Stop();
        IsPlaying = false;
        Position = TimeSpan.Zero;
        SyncUpNext();
        SaveStateNow();
    }

    private void SyncUpNext()
    {
        UpNext.Clear();
        foreach (var t in _queue.UpNext) UpNext.Add(t);
    }

    private void OnPlayerPosition(object? sender, TimeSpan position) => _marshal(() =>
    {
        Position = position;
        // Lock-screen pause, audio-focus loss and headphone unplug pause the engine
        // without going through TogglePlayPause; the tick is where the UI catches up.
        IsPlaying = _player.State == PlaybackState.Playing;
        _consecutiveErrors = 0;
        if ((DateTime.UtcNow - _lastSaveUtc).TotalSeconds >= SaveIntervalSeconds)
            SaveStateNow();
    });

    private void OnPlayerDuration(object? sender, TimeSpan duration) => _marshal(() => Duration = duration);

    private void OnPlayerTrackEnded(object? sender, EventArgs e) => _marshal(() =>
    {
        var next = _queue.Advance(QueueAdvance.Natural);
        if (next == null) StopPlayback(); else StartTrack(next, null);
    });

    private void OnPlayerError(object? sender, string message) => _marshal(() =>
    {
        DebugLog.Write("Audio", $"Playback error: {message} — track: {CurrentTrack?.Title ?? "(none)"}");
        ErrorText = message;
        if (++_consecutiveErrors >= MaxConsecutiveErrors)
        {
            StopPlayback();
            return;
        }
        var next = _queue.Advance(QueueAdvance.UserSkip);
        if (next == null) StopPlayback(); else StartTrack(next, null);
    });

    /// <summary>Queue + position → queue.json (the desktop's QueueState shape).</summary>
    public Task SaveStateAsync()
    {
        var snapshot = _queue.Snapshot();
        var state = new QueueState
        {
            CurrentTrackId = snapshot.CurrentId,
            PositionSeconds = Position.TotalSeconds,
            UpNextIds = snapshot.UpNextIds.ToList(),
            HistoryIds = snapshot.HistoryIds.ToList(),
            RepeatCycleIds = snapshot.RepeatCycleIds.ToList(),
            RepeatMode = snapshot.RepeatMode,
            IsShuffleEnabled = snapshot.IsShuffleEnabled,
            IsMuted = _player.IsMuted,
        };
        return _persistence.SaveQueueStateAsync(state);
    }

    private void SaveStateNow()
    {
        _lastSaveUtc = DateTime.UtcNow;
        _ = SaveStateAsync().ContinueWith(
            t => DebugLog.Write("Queue", $"Save failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Cold start: bring the queue back paused at the saved position. Never auto-plays.</summary>
    public async Task RestoreStateAsync()
    {
        var state = await _persistence.LoadQueueStateAsync();
        if (state == null) return;

        _queue = PlaybackQueue.Restore(
            new PlaybackQueueState(state.CurrentTrackId, state.UpNextIds, state.HistoryIds, state.RepeatCycleIds,
                state.RepeatMode, state.IsShuffleEnabled, Array.Empty<Guid>()),
            id => _library.GetTrackById(id));
        RepeatMode = state.RepeatMode;
        IsShuffleEnabled = state.IsShuffleEnabled;
        CurrentTrack = _queue.Current;
        Duration = _queue.Current?.Duration ?? TimeSpan.Zero;
        Position = _queue.Current != null ? TimeSpan.FromSeconds(Math.Max(0, state.PositionSeconds)) : TimeSpan.Zero;
        IsPlaying = false;
        SyncUpNext();
    }

    public void Dispose()
    {
        _player.PositionChanged -= OnPlayerPosition;
        _player.DurationResolved -= OnPlayerDuration;
        _player.TrackEnded -= OnPlayerTrackEnded;
        _player.PlaybackError -= OnPlayerError;
    }
}
