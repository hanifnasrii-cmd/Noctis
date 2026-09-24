using Noctis.Services;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Audio cutting out mid-track (owner log, 2026-09-23): the library sits on a hard disk that
/// other processes were hammering, VLC's reads stalled for 3.6–8 s, and the engine's only
/// protection was the 1 s file-caching window — everything past it was dropped as "too late".
/// Once a stall is seen, media opened afterwards read further ahead for the session.
/// </summary>
public class VlcReadAheadTests
{
    [Theory]
    [InlineData(1000, 500, 1000)]      // sub-second gap: ordinary jitter, leave it alone
    [InlineData(1000, 999, 1000)]
    [InlineData(1000, 1200, 2500)]     // 1.2 s stall + 1 s margin, rounded up to 500 ms
    [InlineData(1000, 3624, 5000)]     // the PtsGap from the owner's log
    [InlineData(1000, 6737, 8000)]     // 6.7 s stall would want 8 s: capped there
    [InlineData(5000, 1200, 5000)]     // never lowers an already raised value
    [InlineData(1000, 121926, 1000)]   // the whole process was frozen (system sleep), not the disk
    [InlineData(1000, -50, 1000)]
    [InlineData(10000, 3000, 10000)]   // NOCTIS_CACHING above the cap stays as configured
    public void NextReadAheadMs_GrowsToCoverTheStall_WithinBounds(int currentMs, double gapMs, int expected)
    {
        Assert.Equal(expected, VlcAudioPlayer.NextReadAheadMs(currentMs, gapMs));
    }

    [Fact]
    public void ReadAheadOption_IsOnlyEmittedOnceTheValueWasRaised()
    {
        Assert.Null(VlcAudioPlayer.ReadAheadOption(1000, 1000));
        Assert.Equal(":file-caching=5000", VlcAudioPlayer.ReadAheadOption(1000, 5000));
    }
}
