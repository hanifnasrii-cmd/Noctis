using Noctis.Services;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Discord (Mistery, 2026-09-22): Noctis window see-through after sleep on Wayland + NVIDIA,
/// unchanged by software rendering. The resume-time window remap is aimed at exactly that
/// setup, with an env override for testing elsewhere.
/// </summary>
public class LinuxResumeWatcherTests
{
    [Theory]
    [InlineData(null, "wayland", true, true)]    // the reported setup
    [InlineData(null, "Wayland", true, true)]
    [InlineData(null, "x11", true, false)]       // native X11 was not reported; no flash for them
    [InlineData(null, "wayland", false, false)]  // Mesa/AMD/Intel under XWayland keep their memory
    [InlineData(null, null, true, false)]
    [InlineData("1", "x11", false, true)]        // forced on for testing
    [InlineData("0", "wayland", true, false)]    // forced off
    [InlineData("", "wayland", true, true)]      // empty override = unset
    public void ShouldRemapAfterResume_TargetsXWaylandOnNvidia_UnlessOverridden(
        string? overrideEnv, string? sessionType, bool nvidia, bool expected)
    {
        Assert.Equal(expected, LinuxResumeWatcher.ShouldRemapAfterResume(overrideEnv, sessionType, nvidia));
    }

    [Fact]
    public void TryStart_OffLinux_ReturnsNull()
    {
        if (OperatingSystem.IsLinux()) return; // the Linux path needs a system bus; covered by hand
        Assert.Null(LinuxResumeWatcher.TryStart(() => { }));
    }
}
