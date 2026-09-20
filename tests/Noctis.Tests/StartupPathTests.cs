using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Noctis.Services.AudioCd;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Pins the launch-path decisions behind the "Noctis takes long to open" reports:
/// what is precompiled at publish time, what is templated inside MainWindow's
/// InitializeComponent, and what the main view-model graph does synchronously on
/// the UI thread before the first frame.
/// </summary>
public class StartupPathTests
{
    [Fact]
    public void Publish_PrecompilesWithReadyToRun_OnEveryPublishPath()
    {
        var root = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "src", "Noctis", "Noctis.csproj"));
        Assert.Contains("<PublishReadyToRun>true</PublishReadyToRun>", csproj);

        // The local Windows script used to ship a compressed single-file build, which
        // extracts to a temp folder on every first launch. CI never did; keep them aligned.
        var bat = File.ReadAllText(Path.Combine(root, "publish-windows.bat"));
        Assert.Contains("-p:PublishSingleFile=false", bat);
        Assert.DoesNotContain("EnableCompressionInSingleFile=true", bat);
    }

    [Fact]
    public void MainWindow_DoesNotTemplateTheLyricsPanel_UntilItOpens()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Noctis", "Views", "MainWindow.axaml"));
        Assert.DoesNotContain("<views:LyricsPanelView", xaml);
        Assert.Contains("x:Name=\"LyricsPanelHost\"", xaml);
        // Settings went through the same treatment earlier; keep it that way.
        Assert.DoesNotContain("<views:SettingsView", xaml);
    }

    [Fact]
    public void Program_SkipsTheDriveProbe_WhenBuildingTheAudioCdService()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Noctis", "Program.cs"));
        Assert.Contains("probeOnConstruct: false", program);
    }

    [Fact]
    public async Task AudioCd_DeferredProbe_FindsTheDriveOnTheFirstWatchTick()
    {
        var probe = new StubProbe { Roots = { @"D:\" } };
        using var svc = new AudioCdService(probe, new StubReader(), isWindows: true, isSupported: true,
            probeOnConstruct: false);

        // Nothing enumerated in the constructor...
        Assert.False(svc.HasDrive);
        Assert.Equal(0, probe.Calls);

        // ...but the watch timer fires straight away instead of waiting a full interval.
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.DriveStateChanged += (_, _) => changed.TrySetResult(true);
        svc.StartWatching();
        var done = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(changed.Task, done);
        Assert.True(svc.HasDrive);
    }

    [Fact]
    public void AudioCd_DefaultCtor_StillProbesEagerly_ForCallersThatExpectIt()
    {
        var probe = new StubProbe { Roots = { @"D:\" } };
        using var svc = new AudioCdService(probe, new StubReader(), isWindows: true, isSupported: true);
        Assert.True(svc.HasDrive);
    }

    private sealed class StubProbe : IAudioCdDriveProbe
    {
        public List<string> Roots { get; } = new();
        public int Calls { get; private set; }
        public bool SupportsReadyProbe => false;
        public IReadOnlyList<string> GetOpticalDriveRoots() { Calls++; return Roots.ToArray(); }
        public bool IsDiscReady(string driveRoot) => false;
    }

    private sealed class StubReader : IAudioCdReader
    {
        public Task<AudioCdDisc?> ReadAsync(string driveRoot, string mrl, System.Threading.CancellationToken ct = default)
            => Task.FromResult<AudioCdDisc?>(null);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Noctis.sln"))
                || Directory.Exists(Path.Combine(dir.FullName, "src", "Noctis", "Assets", "Icons")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root from " + AppContext.BaseDirectory);
    }
}
