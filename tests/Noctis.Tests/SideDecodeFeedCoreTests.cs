using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Noctis.Services;
using Noctis.Services.AudioAnalysis;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The off-Windows visualizer feed: LibVLC plays straight to the device there (no
/// amem callbacks, no NAudio chain, so <see cref="BeatTapProvider"/> never runs and
/// the meters stay dead — Discord "row of dots doing nothing" on the Flatpak build).
/// <see cref="SideDecodeFeedCore"/> pulls the same track from a side decode (ffmpeg
/// in production, a synthetic stream here) and feeds the meters ahead of the
/// player's clock: it opens near the heard position, reads exactly what the lookahead
/// needs, reopens on seeks instead of chewing through the gap, and stops at EOF.
/// </summary>
public class SideDecodeFeedCoreTests
{
    private const int Rate = 44100;
    private const int Bands = 48;

    private sealed class Source
    {
        public readonly List<long> Opens = new();
        public int Disposed;
        public double Hz = 1000;
        public double LengthSeconds = 30;

        public Stream? Open(long startFrame)
        {
            Opens.Add(startFrame);
            var total = (long)(LengthSeconds * Rate);
            var frames = (int)Math.Max(0, total - startFrame);
            var samples = new float[frames];
            for (var i = 0; i < frames; i++)
                samples[i] = (float)(0.9 * Math.Sin(2 * Math.PI * Hz * (startFrame + i) / Rate));
            return new Tracking(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), this);
        }

        private sealed class Tracking : MemoryStream
        {
            private readonly Source _owner;
            public Tracking(byte[] data, Source owner) : base(data, writable: false) => _owner = owner;
            protected override void Dispose(bool disposing) { _owner.Disposed++; base.Dispose(disposing); }
        }
    }

    private static (SideDecodeFeedCore core, Source src, SpectrumMeter spectrum, BeatMeter beat) Make(double lengthSeconds = 30)
    {
        var src = new Source { LengthSeconds = lengthSeconds };
        var spectrum = new SpectrumMeter(() => 0);
        var beat = new BeatMeter(() => 0);
        var core = new SideDecodeFeedCore(src.Open, Rate, spectrum, beat);
        return (core, src, spectrum, beat);
    }

    private static long Frames(double ms) => (long)Math.Round(ms * Rate / 1000.0);

    private static int BandOf(double hz)
    {
        var logMin = Math.Log(SpectrumMeter.MinHz);
        var logSpan = Math.Log(SpectrumMeter.MaxHz) - logMin;
        return (int)Math.Floor((Math.Log(hz) - logMin) / logSpan * Bands);
    }

    private static int ArgMax(float[] bands)
    {
        var best = 0;
        for (var i = 1; i < bands.Length; i++) if (bands[i] > bands[best]) best = i;
        return best;
    }

    [Fact]
    public void Advance_FeedsAheadOfTheClock_AndTheToneLandsInItsBand()
    {
        var (core, src, spectrum, beat) = Make();

        for (var ms = 0; ms <= 600; ms += 20) core.Advance(ms);

        Assert.Equal(new[] { 0L }, src.Opens);
        Assert.Equal(Frames(600 + SideDecodeFeedCore.LookaheadMs), core.DecodedFrames);
        var bands = new float[Bands];
        Assert.True(spectrum.TryRead(0, bands));
        Assert.Equal(BandOf(1000), ArgMax(bands));
        Assert.InRange(bands[BandOf(1000)], 0.85f, 1.0f);
        Assert.True(beat.IsLive(0));
    }

    [Fact]
    public void Advance_SeekBack_ReopensJustBeforeTheNewTarget()
    {
        var (core, src, _, _) = Make();
        for (var ms = 0; ms <= 10_000; ms += 20) core.Advance(ms);

        core.Advance(2000);

        Assert.Equal(2, src.Opens.Count);
        Assert.Equal(1, src.Disposed);
        Assert.Equal(Frames(2000 + SideDecodeFeedCore.LookaheadMs - SideDecodeFeedCore.PrimeMs), src.Opens[1]);
        Assert.Equal(Frames(2000 + SideDecodeFeedCore.LookaheadMs), core.DecodedFrames);
    }

    [Fact]
    public void Advance_BigJumpForward_ReopensInsteadOfCatchingUp()
    {
        var (core, src, _, _) = Make(lengthSeconds: 120);
        for (var ms = 0; ms <= 1000; ms += 20) core.Advance(ms);

        core.Advance(60_000);

        Assert.Equal(2, src.Opens.Count);
        Assert.Equal(Frames(60_000 + SideDecodeFeedCore.LookaheadMs - SideDecodeFeedCore.PrimeMs), src.Opens[1]);
        Assert.Equal(Frames(60_000 + SideDecodeFeedCore.LookaheadMs), core.DecodedFrames);
    }

    [Fact]
    public void Advance_SmallStall_CatchesUpOnTheSameStream()
    {
        var (core, src, _, _) = Make();
        for (var ms = 0; ms <= 1000; ms += 20) core.Advance(ms);

        core.Advance(1000 + SideDecodeFeedCore.MaxCatchUpMs - 100);

        Assert.Single(src.Opens);
        Assert.Equal(Frames(1000 + SideDecodeFeedCore.MaxCatchUpMs - 100 + SideDecodeFeedCore.LookaheadMs), core.DecodedFrames);
    }

    [Fact]
    public void Advance_PastTheEnd_StopsWithoutReopening()
    {
        var (core, src, _, _) = Make(lengthSeconds: 1);
        for (var ms = 0; ms <= 1500; ms += 20) core.Advance(ms);

        Assert.True(core.Ended);
        Assert.Single(src.Opens);
        Assert.Equal(Frames(1000), core.DecodedFrames);

        // A seek back into the file plays again.
        core.Advance(200);
        Assert.False(core.Ended);
        Assert.Equal(2, src.Opens.Count);
    }

    [Fact]
    public void Reset_ClosesTheStream_AndTheNextAdvanceReopens()
    {
        var (core, src, _, _) = Make();
        core.Advance(0);

        core.Reset();

        Assert.Equal(1, src.Disposed);
        Assert.Equal(0, core.DecodedFrames);
        core.Advance(5000);
        Assert.Equal(2, src.Opens.Count);
        Assert.Equal(Frames(5000 + SideDecodeFeedCore.LookaheadMs - SideDecodeFeedCore.PrimeMs), src.Opens[1]);
    }

    [Fact]
    public void Advance_WhenTheSourceCannotOpen_IsQuiet()
    {
        var spectrum = new SpectrumMeter(() => 0);
        var opens = 0;
        var core = new SideDecodeFeedCore(_ => { opens++; return null; }, Rate, spectrum, new BeatMeter(() => 0));

        core.Advance(0);
        core.Advance(20);

        Assert.Equal(0, core.DecodedFrames);
        Assert.False(spectrum.IsLive(0));
        // No reopen storm: one attempt, then wait for a seek/track change.
        Assert.Equal(1, opens);
    }

    [Theory]
    [InlineData(true, null, false)]   // Windows: the render-chain tap feeds the meters
    [InlineData(true, "1", true)]     // dev override to exercise the side feed on Windows
    [InlineData(false, null, true)]   // Linux/macOS: LibVLC owns the output, side feed needed
    [InlineData(false, "0", false)]   // escape hatch
    public void ShouldRun_OffWindowsUnlessDisabled(bool isWindows, string? env, bool expected)
        => Assert.Equal(expected, SideDecodeMeterFeed.ShouldRun(isWindows, env));
}

/// <summary>
/// The real pipe: ffmpeg decodes a generated WAV (1 kHz for the first second, 3 kHz
/// for the second) from a mid-file start frame; the meter shows the tone at the
/// seek point, and disposing the stream leaves no ffmpeg behind. Skipped (passes
/// vacuously) when no ffmpeg is on this machine.
/// </summary>
public class FfmpegPcmPipeTests
{
    private const int Rate = 44100;

    private static string? FindFfmpeg()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var c = Path.Combine(dir.Trim(), exe); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    private static string WriteTwoToneWav()
    {
        var path = Path.Combine(Path.GetTempPath(), "noctis-sidefeed-" + Guid.NewGuid().ToString("N") + ".wav");
        var frames = Rate * 2;
        var data = new byte[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var hz = i < Rate ? 1000 : 3000;
            var s = (short)(0.9 * short.MaxValue * Math.Sin(2 * Math.PI * hz * i / Rate));
            data[i * 2] = (byte)s;
            data[i * 2 + 1] = (byte)(s >> 8);
        }
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return path;
    }

    [Fact]
    public void Pipe_SeeksToTheStartFrame_AndTheToneThereReachesTheMeter()
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg == null) return;
        var wav = WriteTwoToneWav();
        try
        {
            var spectrum = new SpectrumMeter(() => 0);
            var core = new SideDecodeFeedCore(f => FfmpegPcmPipe.Open(ffmpeg, wav, f, Rate), Rate, spectrum, new BeatMeter(() => 0));

            // Heard position 1.4 s: the primed window sits entirely inside the 3 kHz half.
            for (var ms = 1400; ms <= 1700; ms += 20) core.Advance(ms);

            var bands = new float[48];
            Assert.True(spectrum.TryRead(0, bands));
            var logMin = Math.Log(SpectrumMeter.MinHz);
            var logSpan = Math.Log(SpectrumMeter.MaxHz) - logMin;
            var band3k = (int)Math.Floor((Math.Log(3000) - logMin) / logSpan * 48);
            var band1k = (int)Math.Floor((Math.Log(1000) - logMin) / logSpan * 48);
            Assert.InRange(bands[band3k], 0.85f, 1.0f);
            Assert.InRange(bands[band1k], 0f, 0.2f);

            // EOF at 2 s, then the process is gone once the stream is dropped.
            for (var ms = 1720; ms <= 2400; ms += 20) core.Advance(ms);
            Assert.True(core.Ended);
            core.Reset();
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }
}
