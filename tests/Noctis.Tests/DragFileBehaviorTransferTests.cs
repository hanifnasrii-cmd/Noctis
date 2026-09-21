using Avalonia.Platform.Storage;
using Noctis.Helpers;
using Noctis.Models;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The in-app drag payload rides the DataTransfer API (Avalonia 11.3+, the only
/// API left in 12): the transfer carries a token string, the objects stay in a
/// per-drag slot. These pin the round trip and the stale-token guard.
/// </summary>
public class DragFileBehaviorTransferTests
{
    [Fact]
    public void Tracks_transfer_round_trips_the_same_track_instances()
    {
        var tracks = new List<Track> { new() { Title = "A" }, new() { Title = "B" } };

        using var transfer = DragFileBehavior.BuildTracksTransfer(tracks, Array.Empty<IStorageItem>());

        Assert.True(DragFileBehavior.IsInternalDrag(transfer));
        var back = DragFileBehavior.GetDraggedTracks(transfer);
        Assert.NotNull(back);
        Assert.Same(tracks[0], back![0]);
        Assert.Same(tracks[1], back[1]);
        Assert.Null(DragFileBehavior.GetDraggedPlaylistId(transfer));
    }

    [Fact]
    public void Playlist_transfer_round_trips_the_id()
    {
        var id = Guid.NewGuid();

        using var transfer = DragFileBehavior.BuildPlaylistTransfer(id);

        Assert.True(DragFileBehavior.IsInternalDrag(transfer));
        Assert.Equal(id, DragFileBehavior.GetDraggedPlaylistId(transfer));
        Assert.Null(DragFileBehavior.GetDraggedTracks(transfer));
    }

    [Fact]
    public void Stale_token_from_an_earlier_drag_yields_nothing()
    {
        using var first = DragFileBehavior.BuildTracksTransfer(
            new List<Track> { new() { Title = "old" } }, Array.Empty<IStorageItem>());
        using var newer = DragFileBehavior.BuildPlaylistTransfer(Guid.NewGuid()); // replaces the slot

        Assert.Null(DragFileBehavior.GetDraggedTracks(first));
    }
}
