using Noctis.Services;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Refreshing the library while a configured folder is unreachable (drive offline,
/// letter changed after a partition change) makes the library keep what it has and
/// raise ScanAborted. Nothing listened to that event, so Settings ended the refresh
/// with "N tracks found." as if the folder were fine — a user whose playback had
/// just gone silent (Discord, Mapletic, 09-20) pressed Refresh and learned nothing.
/// The status line must now name the unreachable folder and stay up.
/// </summary>
public class SettingsScanAbortedStatusTests
{
    private sealed class NoOpPlayHistory : IPlayHistoryService
    {
        public IReadOnlyList<Noctis.Models.PlayHistoryEvent> Events => Array.Empty<Noctis.Models.PlayHistoryEvent>();
        public Task PreloadAsync() => Task.CompletedTask;
        public void RecordPlay(Noctis.Models.Track track) { }
        public void RecordSkip(Noctis.Models.Track track) { }
        public Task FlushAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Rescan_WhenTheLibraryAbortsOnAnUnreachableRoot_SaysSoInsteadOfTracksFound()
    {
        using var persistence = new TestPersistenceService();
        var lib = new FakeLibraryService { AbortScanWithRoots = new[] { @"A:\Music" } };
        var vm = new SettingsViewModel(persistence, lib, new NoOpPlayHistory());

        await vm.RescanCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.ScanAbortedStatus(new[] { @"A:\Music" }), vm.ScanStatusText);
        Assert.Contains(@"A:\Music", vm.ScanStatusText);
        Assert.DoesNotContain("tracks found", vm.ScanStatusText);
        Assert.False(vm.IsScanning);

        // The message must not auto-clear like the happy-path status does.
        await Task.Delay(3300);
        Assert.Contains(@"A:\Music", vm.ScanStatusText);
    }

    [Fact]
    public async Task Rescan_AfterTheRootIsBack_ReportsTracksFoundAgain()
    {
        using var persistence = new TestPersistenceService();
        var lib = new FakeLibraryService { AbortScanWithRoots = new[] { @"A:\Music" } };
        var vm = new SettingsViewModel(persistence, lib, new NoOpPlayHistory());

        await vm.RescanCommand.ExecuteAsync(null);
        Assert.Contains(@"A:\Music", vm.ScanStatusText);

        // Drive is back: the next refresh scans normally and the stale warning goes.
        lib.AbortScanWithRoots = null;
        await vm.RescanCommand.ExecuteAsync(null);
        Assert.Equal("No tracks found.", vm.ScanStatusText);
    }
}
