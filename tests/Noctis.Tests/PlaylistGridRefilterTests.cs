using System.Collections.Specialized;
using Noctis.Models;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Navigating to the Playlists section calls ApplyFilter("") on the view model directly
/// (MainWindowViewModel, "Clear it directly here"). That used to Clear+refill
/// FilteredPlaylists, and the Reset dropped every tile container — the rebuilt CachedImages
/// start blank and only repaint once the cache hit lands, so the whole grid of artwork
/// blinked on every click of the section. These pin the in-place sync that replaced it.
/// </summary>
public class PlaylistGridRefilterTests
{
    private static LibraryPlaylistsViewModel Build(out SidebarViewModel sidebar, int count)
    {
        var library = new FakeLibraryService();
        var persistence = new TestPersistenceService();
        sidebar = new SidebarViewModel(persistence, library);
        var player = new PlayerViewModel(new FakeAudioPlayer(), library, persistence, new FakeAnimatedCoverService());
        var vm = new LibraryPlaylistsViewModel(sidebar, player, library, persistence);

        for (var i = 0; i < count; i++)
            sidebar.PlaylistItems.Add(new PlaylistNavItem
            {
                Key = $"playlist:{i}",
                Label = $"Playlist {i}",
                PlaylistId = Guid.NewGuid(),
                TrackCount = 10,
            });

        vm.Refresh();
        return vm;
    }

    [Fact]
    public void ReApplyingTheSameFilter_DoesNotTouchTheCollection()
    {
        var vm = Build(out _, 6);
        Assert.Equal(6, vm.FilteredPlaylists.Count);

        var events = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)vm.FilteredPlaylists).CollectionChanged += (_, e) => events.Add(e.Action);

        // What a click on the Playlists section does.
        vm.ApplyFilter(string.Empty);

        Assert.Empty(events);
        Assert.Equal(6, vm.FilteredPlaylists.Count);
    }

    [Fact]
    public void ReApplyingTheSameFilter_KeepsTheSameItemInstancesInOrder()
    {
        var vm = Build(out _, 6);
        var before = vm.FilteredPlaylists.ToList();

        vm.ApplyFilter(string.Empty);

        Assert.Equal(before.Count, vm.FilteredPlaylists.Count);
        for (var i = 0; i < before.Count; i++)
            Assert.Same(before[i], vm.FilteredPlaylists[i]);
    }

    [Fact]
    public void NarrowingAndWideningTheFilter_StillProducesTheRightSet()
    {
        var vm = Build(out var sidebar, 6);

        vm.ApplyFilter("Playlist 3");
        Assert.Single(vm.FilteredPlaylists);
        Assert.Equal("Playlist 3", vm.FilteredPlaylists[0].Label);
        Assert.True(vm.ShowNoResults is false);

        vm.ApplyFilter(string.Empty);
        Assert.Equal(6, vm.FilteredPlaylists.Count);
        Assert.Equal(sidebar.PlaylistItems.Select(p => p.Label), vm.FilteredPlaylists.Select(p => p.Label));

        vm.ApplyFilter("nothing matches this");
        Assert.Empty(vm.FilteredPlaylists);
        Assert.True(vm.ShowNoResults);
    }

    [Fact]
    public void ReorderingBySort_MovesItemsRatherThanRebuilding()
    {
        var vm = Build(out _, 6);
        var before = vm.FilteredPlaylists.ToHashSet();

        vm.SetSortCommand.Execute("name");
        var after = vm.FilteredPlaylists.ToHashSet();

        // Same instances, so the containers (and their decoded covers) survive a sort.
        Assert.Equal(before, after);
    }
}
