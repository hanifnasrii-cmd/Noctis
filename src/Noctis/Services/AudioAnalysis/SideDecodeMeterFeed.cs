using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Noctis.Models;

namespace Noctis.Services.AudioAnalysis;

/// <summary>
/// Feeds <see cref="SpectrumMeter"/> and <see cref="BeatMeter"/> from a SIDE decode
/// of the playing track, for the platforms where the render chain never sees PCM.
///
/// On Windows the meters are fed by <see cref="BeatTapProvider"/> inside the NAudio
/// output chain (gapless engine / WASAPI sink). On Linux and macOS LibVLC plays
/// straight to the device — no amem callbacks, no managed chain — so nothing ever
/// called <c>Feed</c> and the visualizer, the EQ bars, Kawarp's beat pulse and the
/// plugin audio API all sat at rest (Discord: "a row of dots doing nothing" on the
/// Flatpak build). This decodes the same file through ffmpeg (already bundled for
/// the converter / BPM analysis) and pushes samples a little ahead of the player's
/// clock, so the meters' own latency alignment lands the picture on the ear.
///
/// The pure media-time logic lives in <see cref="SideDecodeFeedCore"/> (unit-tested);
/// this class only owns the thread, the player polling and the ffmpeg process.
/// </summary>
public sealed class SideDecodeMeterFeed : IDisposable
{
    /// <summary>Decode rate: Nyquist must clear <see cref="SpectrumMeter.MaxHz"/>; the
    /// meters' block/window sizes were tuned around 44.1/48 kHz.</summary>
    public const int SampleRate = 44100;

    private const int TickMs = 20;

    private readonly IAudioPlayer _player;
    private readonly IAudioConverterService _ffmpeg;
    private readonly LyricsPlaybackClock _clock = new();
    private Thread? _thread;
    private volatile bool _stop;
    private bool _loggedMissingFfmpeg;

    public SideDecodeMeterFeed(IAudioPlayer player, IAudioConverterService ffmpeg)
    {
        _player = player;
        _ffmpeg = ffmpeg;
    }

    /// <summary>
    /// Windows has the render-chain tap, so the side feed would double-write the
    /// meters there; <c>NOCTIS_SIDE_FEED=1</c> forces it on for development (pair it
    /// with <c>NOCTIS_GAPLESS_ENGINE=0</c>) and <c>=0</c> is the escape hatch elsewhere.
    /// </summary>
    public static bool ShouldRun(bool isWindows, string? env)
        => env == "1" || (!isWindows && env != "0");

    public void Start()
    {
        if (_thread != null) return;
        if (!ShouldRun(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("NOCTIS_SIDE_FEED")))
            return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "side-decode-feed" };
        _thread.Start();
        DebugLogger.Info(DebugLogger.Category.Playback, "SideDecodeFeed.Start", $"rate={SampleRate}");
    }

    private void Loop()
    {
        string? mediaPath = null;
        string? ffmpegPath = null;
        var isLocalFile = false;
        var core = new SideDecodeFeedCore(OpenAt, SampleRate, SpectrumMeter.Shared, BeatMeter.Shared);
        try
        {
            while (!_stop)
            {
                try
                {
                    var path = _player.CurrentMediaPath;
                    if (!string.Equals(path, mediaPath, StringComparison.Ordinal))
                    {
                        mediaPath = path;
                        core.Reset();
                        _clock.Reset();
                        // Streams / audio CDs have no file for ffmpeg to open.
                        isLocalFile = path != null && File.Exists(path);
                        // Re-resolve per track so a path set later in Settings is picked up.
                        ffmpegPath = _ffmpeg.GetFfmpegPath();
                        if (ffmpegPath == null && !_loggedMissingFfmpeg)
                        {
                            _loggedMissingFfmpeg = true;
                            DebugLogger.Warn(DebugLogger.Category.Playback, "SideDecodeFeed.NoFfmpeg",
                                "visualizer feed needs ffmpeg (Settings → Advanced → Helper programs)");
                        }
                    }

                    if (_player.State == PlaybackState.Playing && isLocalFile && ffmpegPath != null)
                    {
                        var raw = _player.Position.TotalMilliseconds;
                        var now = Stopwatch.GetElapsedTime(0).TotalMilliseconds;
                        var heard = _clock.Sample(raw, now) - _player.OutputLatency.TotalMilliseconds;
                        core.Advance(Math.Max(0, heard));
                    }
                    else
                    {
                        // Paused/stopped: no feed, the meters go non-live and the bars rest.
                        _clock.Reset();
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Warn(DebugLogger.Category.Playback, "SideDecodeFeed.Tick", ex.Message);
                }
                Thread.Sleep(TickMs);
            }
        }
        finally
        {
            core.Reset();
        }

        Stream? OpenAt(long startFrame)
        {
            var exe = ffmpegPath;
            var path = mediaPath;
            if (exe == null || path == null) return null;
            try
            {
                return FfmpegPcmPipe.Open(exe, path, startFrame, SampleRate);
            }
            catch (Exception ex)
            {
                DebugLogger.Warn(DebugLogger.Category.Playback, "SideDecodeFeed.OpenFailed", ex.Message);
                return null;
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        var t = _thread;
        _thread = null;
        if (t != null && t.IsAlive && Thread.CurrentThread != t)
            t.Join(500);
    }
}

/// <summary>
/// Media-time side of the feed: decides what to decode for a given heard position
/// and hands it to the meters. No threads, no clocks — the meters stamp with their
/// own — so it is driven by tests with a synthetic stream.
/// </summary>
public sealed class SideDecodeFeedCore
{
    /// <summary>How far ahead of the speaker the decode runs; covers tick jitter and
    /// the ffmpeg pipe so the meters never starve.</summary>
    public const int LookaheadMs = 150;

    /// <summary>On (re)open, start this far before the target so the spectrum window
    /// and a few beat blocks are primed instead of reading a cold ring.</summary>
    public const int PrimeMs = 300;

    /// <summary>A gap wider than this between what was decoded and what is needed is a
    /// seek (or a long stall): reopen at the new spot rather than decode the gap.</summary>
    public const int MaxCatchUpMs = 1500;

    private readonly Func<long, Stream?> _openAtFrame;
    private readonly int _rate;
    private readonly SpectrumMeter _spectrum;
    private readonly BeatMeter _beat;
    private readonly float[] _scratch;
    private readonly long _lookaheadFrames;
    private readonly long _primeFrames;
    private readonly long _maxCatchUpFrames;

    private Stream? _stream;
    private long _decoded;
    private bool _ended;
    private bool _openFailed;

    public SideDecodeFeedCore(Func<long, Stream?> openAtFrame, int sampleRate, SpectrumMeter spectrum, BeatMeter beat)
    {
        _openAtFrame = openAtFrame;
        _rate = sampleRate;
        _spectrum = spectrum;
        _beat = beat;
        _lookaheadFrames = Frames(LookaheadMs);
        _primeFrames = Frames(PrimeMs);
        _maxCatchUpFrames = Frames(MaxCatchUpMs);
        _scratch = new float[Math.Max(_maxCatchUpFrames, _primeFrames + _lookaheadFrames)];
    }

    /// <summary>Frames decoded so far on the current stream, as an absolute frame index.</summary>
    public long DecodedFrames => _decoded;

    /// <summary>True once the stream ran dry; cleared by a seek back or <see cref="Reset"/>.</summary>
    public bool Ended => _ended;

    private long Frames(double ms) => (long)Math.Round(ms * _rate / 1000.0);

    /// <summary>
    /// Decode up to <paramref name="heardMs"/> + lookahead and feed the meters.
    /// <paramref name="heardMs"/> is the media time at the speaker right now.
    /// </summary>
    public void Advance(double heardMs)
    {
        var target = Frames(heardMs) + _lookaheadFrames;
        var seekBack = target < _decoded;
        if (_stream == null || seekBack || target - _decoded > _maxCatchUpFrames)
        {
            // A failed open waits for a seek/track change rather than retrying every tick.
            if (_openFailed && !seekBack) return;
            if (_ended && !seekBack) return;
            Reopen(Math.Max(0, target - _primeFrames));
            if (_stream == null) { _openFailed = true; return; }
        }
        if (_ended) return;

        var want = (int)(target - _decoded);
        if (want <= 0) return;
        var got = ReadFrames(want);
        if (got > 0)
        {
            // The chunk ends `lookahead` ahead of the ear: the spectrum aligns on its
            // head; the beat meter stamps blocks from the chunk START, so it gets the
            // start's lead (negative while a primed chunk reaches into the past).
            var startLeadMs = (int)Math.Round((_decoded - Frames(heardMs)) * 1000.0 / _rate);
            _decoded += got;
            var headLeadMs = (int)Math.Round((_decoded - Frames(heardMs)) * 1000.0 / _rate);
            try
            {
                _beat.Feed(_scratch, 0, got, 1, _rate, startLeadMs);
                _spectrum.Feed(_scratch, 0, got, 1, _rate, headLeadMs);
            }
            catch
            {
                // A visual nicety must never take the feed thread down.
            }
        }
        if (got < want) _ended = true;
    }

    /// <summary>Drop the stream (track change / stop); the next <see cref="Advance"/> reopens.</summary>
    public void Reset()
    {
        CloseStream();
        _decoded = 0;
        _ended = false;
        _openFailed = false;
    }

    private void Reopen(long startFrame)
    {
        CloseStream();
        _ended = false;
        _openFailed = false;
        _stream = _openAtFrame(startFrame);
        _decoded = startFrame;
    }

    private void CloseStream()
    {
        var s = _stream;
        _stream = null;
        if (s == null) return;
        try { s.Dispose(); } catch { }
    }

    /// <summary>Blocking read of exactly <paramref name="frames"/> f32 samples (fewer at EOF).</summary>
    private int ReadFrames(int frames)
    {
        var bytes = MemoryMarshal.AsBytes(_scratch.AsSpan(0, frames));
        var total = 0;
        while (total < bytes.Length)
        {
            int n;
            try { n = _stream!.Read(bytes.Slice(total)); }
            catch { n = 0; }
            if (n <= 0) break;
            total += n;
        }
        return total / sizeof(float);
    }
}

/// <summary>
/// ffmpeg as a PCM pipe: mono f32le at the requested rate from a start frame, read
/// off stdout. Disposing the stream kills the process (ffmpeg would otherwise sit
/// blocked on a full pipe forever).
/// </summary>
internal static class FfmpegPcmPipe
{
    public static Stream? Open(string ffmpeg, string mediaPath, long startFrame, int rate)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var startSeconds = startFrame / (double)rate;
        foreach (var a in new[]
        {
            "-nostats", "-hide_banner", "-loglevel", "error",
            // -ss before -i: demuxer seek then decode-and-discard to the exact point.
            "-ss", startSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            "-i", mediaPath,
            "-map", "0:a:0", "-ac", "1", "-ar", rate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-f", "f32le", "-"
        }) psi.ArgumentList.Add(a);

        var p = Process.Start(psi);
        if (p == null) return null;
        // Drain stderr so a chatty ffmpeg can never block on that pipe.
        _ = p.StandardError.BaseStream.CopyToAsync(Stream.Null);
        return new ProcessStream(p, p.StandardOutput.BaseStream);
    }

    private sealed class ProcessStream : Stream
    {
        private readonly Process _process;
        private readonly Stream _stdout;

        public ProcessStream(Process process, Stream stdout)
        {
            _process = process;
            _stdout = stdout;
        }

        public override int Read(Span<byte> buffer) => _stdout.Read(buffer);
        public override int Read(byte[] buffer, int offset, int count) => _stdout.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (!_process.HasExited) _process.Kill(true); } catch { }
                try { _stdout.Dispose(); } catch { }
                try { _process.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
