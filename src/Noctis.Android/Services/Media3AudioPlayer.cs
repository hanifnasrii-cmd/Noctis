using Android.Content;
using Android.Media;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using Avalonia.Threading;
using Noctis.Models;
using Noctis.Services;
using AUri = Android.Net.Uri;
using JFile = Java.IO.File;
// Android.Media also defines AudioAttributes and MediaMetadata; alias the Media3 ones we
// mean (same pattern as AUri/JFile above) rather than fully-qualifying every use.
using AudioAttributes = AndroidX.Media3.Common.AudioAttributes;
using MediaMetadata = AndroidX.Media3.Common.MediaMetadata;

namespace Noctis.Android.Services;

/// <summary>
/// IAudioPlayer over Media3 ExoPlayer. Main-thread only (ExoPlayer's application looper
/// is the Android main thread, which is also Avalonia's UI thread here), so no locking
/// and no marshalling: listener callbacks and the position timer already run there.
/// Gapless: the next file is appended to ExoPlayer's item list by PrepareNext; the
/// automatic transition raises TrackEnded, and the ViewModel's Play(next) is answered
/// without a restart because the path matches the item already playing.
/// </summary>
public sealed class Media3AudioPlayer : IAudioPlayer
{
    private const int PositionPollMs = 250;

    private readonly Context _context;
    private readonly ILibraryService _library;
    private readonly IPersistenceService _persistence;
    private readonly IExoPlayer _player;
    private readonly Listener _listener;
    private readonly DispatcherTimer _positionTimer;

    private Dictionary<string, Track>? _byPath;
    private bool _gapless = true;
    private bool _autoTransitionPending;
    private bool _serviceStarted;
    private bool _disposed;
    private int _volume = 100;
    private int _volumeAdjust;
    private bool _muted;

    public event EventHandler? TrackEnded;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<string>? PlaybackError;
    public event EventHandler<TimeSpan>? DurationResolved;
    public event EventHandler<string>? OutputModeChanged;

    public Media3AudioPlayer(Context context, ILibraryService library, IPersistenceService persistence)
    {
        _context = context;
        _library = library;
        _persistence = persistence;

        var attributes = new AudioAttributes.Builder()
            .SetUsage(C.UsageMedia)
            .SetContentType(C.AudioContentTypeMusic)
            .Build();
        _player = new ExoPlayerBuilder(context)
            .SetAudioAttributes(attributes, true)     // true = Media3 handles audio focus (pause on loss, duck on transient)
            .SetHandleAudioBecomingNoisy(true)        // headphones unplugged → pause
            .SetWakeMode(C.WakeModeLocal)             // keep the CPU awake while playing with the screen off
            .Build();
        _listener = new Listener(this);
        _player.AddListener(_listener);
        PlaybackEngine.Player = _player;

        OutputLatency = EstimateOutputLatency(context);
        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(PositionPollMs), DispatcherPriority.Background, (_, _) => PollPosition());
        _library.LibraryUpdated += OnLibraryUpdated;
    }

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Duration { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan OutputLatency { get; }
    public long CurrentSessionId { get; private set; }
    public string? CurrentMediaPath { get; private set; }

    public int Volume { get => _volume; set { _volume = Math.Clamp(value, 0, 100); ApplyVolume(); } }
    public int VolumeAdjust { get => _volumeAdjust; set { _volumeAdjust = Math.Clamp(value, -100, 100); ApplyVolume(); } }
    public bool IsMuted { get => _muted; set { _muted = value; ApplyVolume(); } }
    public long PendingSeekMs { get; set; } = -1;

    public bool ExclusiveModeActive => false;
    public bool EqualizerActive => false;
    public string OutputDescription => "Android AudioTrack (Media3)";
    public double ReplayGainAppliedDb => 0;

    public void Play(string filePath)
    {
        if (_disposed || string.IsNullOrWhiteSpace(filePath)) return;
        CurrentSessionId++;

        // Gapless handoff already happened inside ExoPlayer: the ViewModel is telling us
        // what we are already playing. Do not restart it.
        if (_autoTransitionPending && _player.CurrentMediaItem?.MediaId == filePath)
        {
            _autoTransitionPending = false;
            CurrentMediaPath = filePath;
            State = PlaybackState.Playing;
            RaiseDurationIfKnown();
            EnsureServiceStarted();
            _positionTimer.Start();
            return;
        }

        _autoTransitionPending = false;
        _player.SetMediaItem(BuildItem(filePath));
        _player.Prepare();
        if (PendingSeekMs >= 0)
        {
            _player.SeekTo(PendingSeekMs);
            PendingSeekMs = -1;
        }
        ApplyVolume();
        _player.Play();
        CurrentMediaPath = filePath;
        Position = TimeSpan.Zero;
        State = PlaybackState.Playing;
        EnsureServiceStarted();
        _positionTimer.Start();
    }

    public void Pause()
    {
        if (_disposed || State != PlaybackState.Playing) return;
        _player.Pause();
        State = PlaybackState.Paused;
    }

    public void Resume()
    {
        if (_disposed || State != PlaybackState.Paused) return;
        _player.Play();
        State = PlaybackState.Playing;
    }

    public void Stop()
    {
        if (_disposed) return;
        _positionTimer.Stop();
        _player.Stop();
        _player.ClearMediaItems();
        _autoTransitionPending = false;
        CurrentMediaPath = null;
        Position = TimeSpan.Zero;
        State = PlaybackState.Stopped;
    }

    public void Seek(TimeSpan position)
    {
        if (_disposed || State == PlaybackState.Stopped) return;
        _player.SeekTo((long)position.TotalMilliseconds);
        Position = position;
        PositionChanged?.Invoke(this, position);
    }

    public void SetGapless(bool enabled)
    {
        _gapless = enabled;
        if (!enabled) CancelPreparedNext();
    }

    public void PrepareNext(string filePath, long startPositionMs = -1)
    {
        if (_disposed || !_gapless || _player.MediaItemCount == 0) return;
        var current = _player.CurrentMediaItemIndex;
        if (_player.MediaItemCount > current + 1 && _player.GetMediaItemAt(current + 1).MediaId == filePath)
            return; // already queued
        TrimAfterCurrent();
        _player.AddMediaItem(BuildItem(filePath));
    }

    public void CancelPreparedNext()
    {
        if (_disposed) return;
        TrimAfterCurrent();
    }

    private void TrimAfterCurrent()
    {
        var current = _player.CurrentMediaItemIndex;
        while (_player.MediaItemCount > current + 1)
            _player.RemoveMediaItem(_player.MediaItemCount - 1);
    }

    public void SetPlaybackRate(double rate)
    {
        if (_disposed) return;
        _player.SetPlaybackSpeed((float)Math.Clamp(rate, 0.5, 2.0));
    }

    public void CommitVolume() { }
    public void SetNormalization(bool enabled) { }
    public void SetExclusiveMode(bool enabled) { }
    public void ApplyReplayGain(string mode, double preampDb) { }
    public void SetCrossfade(bool enabled, int durationSeconds, AutoMixFadeCurve fadeCurve = AutoMixFadeCurve.SmoothEase, bool fadeOut = true, bool overlap = false) { }
    public void SetPitchSemitones(double semitones) { }
    public void SetUpmixMode(string mode) { }
    public void SetAdvancedEqualizer(bool enabled, float[] bands, float preampDb) { }

    // ── ExoPlayer callbacks (main thread) ──

    private void OnPlaybackStateChanged(int playbackState)
    {
        if (playbackState == BasePlayer.InterfaceConsts.StateReady)
        {
            RaiseDurationIfKnown();
        }
        else if (playbackState == BasePlayer.InterfaceConsts.StateEnded)
        {
            // End of the LAST item (an auto-advance to a queued item raises
            // OnMediaItemTransition instead, never Ended).
            _positionTimer.Stop();
            State = PlaybackState.Stopped;
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMediaItemTransition(MediaItem? item, int reason)
    {
        if (reason != BasePlayer.InterfaceConsts.MediaItemTransitionReasonAuto || item == null) return;
        // ExoPlayer moved to the item PrepareNext queued: tell the ViewModel the previous
        // track ended; its Play(next) will match this item and leave it running.
        _autoTransitionPending = true;
        CurrentMediaPath = item.MediaId;
        Position = TimeSpan.Zero;
        TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayerError(PlaybackException error)
    {
        _positionTimer.Stop();
        State = PlaybackState.Stopped;
        PlaybackError?.Invoke(this, $"{error.ErrorCodeName}: {error.Message}");
    }

    private void OnIsPlayingChanged(bool isPlaying)
    {
        // Audio focus loss, headphone unplug and lock-screen Pause all land here without a
        // Pause() call from us. Mirror it so the ViewModel's next toggle does the right thing.
        if (!isPlaying && State == PlaybackState.Playing && _player.PlaybackState == BasePlayer.InterfaceConsts.StateReady && !_player.PlayWhenReady)
            State = PlaybackState.Paused;
        else if (isPlaying && State == PlaybackState.Paused)
            State = PlaybackState.Playing;
    }

    private void PollPosition()
    {
        if (_disposed || CurrentMediaPath == null) return;
        var ms = _player.CurrentPosition;
        if (ms < 0) return;
        Position = TimeSpan.FromMilliseconds(ms);
        PositionChanged?.Invoke(this, Position);
    }

    private void RaiseDurationIfKnown()
    {
        var ms = _player.Duration;
        if (ms == C.TimeUnset || ms <= 0) return;
        Duration = TimeSpan.FromMilliseconds(ms);
        DurationResolved?.Invoke(this, Duration);
    }

    private void ApplyVolume()
    {
        if (_disposed) return;
        var gain = _muted ? 0f : (_volume / 100f) * (1f + _volumeAdjust / 100f);
        _player.Volume = Math.Clamp(gain, 0f, 1f);
    }

    private void EnsureServiceStarted()
    {
        if (_serviceStarted) return;
        // Plain StartService while the activity is visible; Media3 promotes the service to
        // a foreground media service itself once the session's player is playing.
        _context.StartService(new Intent(_context, typeof(NoctisPlaybackService)));
        _serviceStarted = true;
    }

    private MediaItem BuildItem(string path)
    {
        var uri = path.StartsWith("content://", StringComparison.Ordinal)
            ? AUri.Parse(path)!
            : AUri.FromFile(new JFile(path))!;

        var meta = new MediaMetadata.Builder();
        var track = Lookup(path);
        if (track != null)
        {
            meta.SetTitle(track.Title).SetArtist(track.Artist).SetAlbumTitle(track.Album);
            var art = _persistence.GetArtworkPath(track.AlbumId);
            if (File.Exists(art))
                meta.SetArtworkUri(AUri.FromFile(new JFile(art)));
        }
        else
        {
            meta.SetTitle(Path.GetFileNameWithoutExtension(path));
        }

        return new MediaItem.Builder()
            .SetUri(uri)
            .SetMediaId(path)
            .SetMediaMetadata(meta.Build())
            .Build();
    }

    private Track? Lookup(string path)
    {
        _byPath ??= _library.Tracks.GroupBy(t => t.FilePath).ToDictionary(g => g.Key, g => g.First());
        return _byPath.GetValueOrDefault(path);
    }

    private void OnLibraryUpdated(object? sender, EventArgs e) => _byPath = null;

    /// <summary>
    /// ExoPlayer's position already tracks the AudioTrack head, so what remains is the
    /// output buffer the mixer holds: two buffers of the device's native size is the
    /// usual figure. Zero when the properties are unavailable.
    /// </summary>
    private static TimeSpan EstimateOutputLatency(Context context)
    {
        try
        {
            var audio = (AudioManager?)context.GetSystemService(Context.AudioService);
            var frames = int.TryParse(audio?.GetProperty(AudioManager.PropertyOutputFramesPerBuffer), out var f) ? f : 0;
            var rate = int.TryParse(audio?.GetProperty(AudioManager.PropertyOutputSampleRate), out var r) ? r : 0;
            if (frames > 0 && rate > 0)
                return TimeSpan.FromSeconds(2.0 * frames / rate);
        }
        catch (Exception ex)
        {
            DebugLog.Write("Audio", $"Output latency probe failed: {ex.Message}");
        }
        return TimeSpan.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _positionTimer.Stop();
        _library.LibraryUpdated -= OnLibraryUpdated;
        _player.RemoveListener(_listener);
        _player.Release();
        if (ReferenceEquals(PlaybackEngine.Player, _player)) PlaybackEngine.Player = null;
    }

    /// <summary>Java-side listener; every IPlayerListener method has a default body, so only these four are overridden.</summary>
    private sealed class Listener : Java.Lang.Object, IPlayerListener
    {
        private readonly Media3AudioPlayer _owner;
        public Listener(Media3AudioPlayer owner) => _owner = owner;
        public void OnPlaybackStateChanged(int playbackState) => _owner.OnPlaybackStateChanged(playbackState);
        public void OnMediaItemTransition(MediaItem? mediaItem, int reason) => _owner.OnMediaItemTransition(mediaItem, reason);
        public void OnPlayerError(PlaybackException error) => _owner.OnPlayerError(error);
        public void OnIsPlayingChanged(bool isPlaying) => _owner.OnIsPlayingChanged(isPlaying);
    }
}
