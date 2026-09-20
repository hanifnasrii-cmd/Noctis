using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Noctis.Models;
using Noctis.Services.LyricsStudio;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Lyrics Studio › Choose songs: search finds songs and albums, an album row ticks every
/// local song it holds, the format chip is one setting (word timings = ELRC), and Confirm
/// hands back the songs in pick order together with that format.
/// </summary>
public class LyricsStudioPickerViewModelTests
{
    private static Track T(string title, string artist, string album, Guid albumId, SourceType source = SourceType.Local) =>
        new() { Title = title, Artist = artist, Album = album, AlbumId = albumId, SourceType = source, FilePath = $"C:\\m\\{title}.mp3" };

    private static (FakeLibraryService Library, Album Album, Track A, Track B, Track Solo) Library()
    {
        var lib = new FakeLibraryService();
        var albumId = Guid.NewGuid();
        var a = T("Fantasía", "Alex Sensation", "Fantasía - Single", albumId);
        var b = T("Fantasía (Remix)", "Alex Sensation", "Fantasía - Single", albumId);
        var remote = T("Fantasía (Live)", "Alex Sensation", "Fantasía - Single", albumId, SourceType.Navidrome);
        var solo = T("Lean", "Amenazzy", "Lean", Guid.NewGuid());
        lib.TrackList.AddRange(new[] { a, b, remote, solo });
        var album = new Album { Id = albumId, Name = "Fantasía - Single", Artist = "Alex Sensation", Tracks = new List<Track> { a, b, remote } };
        ((List<Album>)lib.Albums).Add(album);
        return (lib, album, a, b, solo);
    }

    [AvaloniaFact]
    public void Search_ListsAlbumsThenSongs_AndAlbumRowTicksItsLocalSongs()
    {
        var (lib, _, a, b, _) = Library();
        var vm = new LyricsStudioPickerViewModel(lib, wordTimings: true,
            detectFormats: tracks => tracks.Select(_ => LyricsFormat.Lrc).ToList());

        Assert.True(vm.ShowPrompt);
        vm.SearchText = "fantas";
        vm.FormatScan.Wait(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs(); // the scan posts its labels to the UI thread

        var albumRow = Assert.Single(vm.Results, r => r.IsAlbum);
        Assert.Equal("2 songs", albumRow.StateText); // the remote track is not offered
        var songRows = vm.Results.Where(r => !r.IsAlbum).ToList();
        Assert.Equal(2, songRows.Count);
        Assert.All(songRows, r => Assert.Equal("line-level", r.StateText));

        vm.ToggleSelectCommand.Execute(albumRow);
        Assert.Equal(2, vm.SelectedCount);
        Assert.True(albumRow.IsSelected);
        Assert.All(songRows, r => Assert.True(r.IsSelected));
        Assert.Equal(new[] { a.Id, b.Id }, vm.PickedTracks.Select(t => t.Id));

        // Unticking one song un-ticks the album row; ticking the album again fills the gap only.
        vm.ToggleSelectCommand.Execute(songRows[1]);
        Assert.False(albumRow.IsSelected);
        Assert.Equal(1, vm.SelectedCount);
        vm.ToggleSelectCommand.Execute(albumRow);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal(new[] { a.Id, b.Id }, vm.PickedTracks.Select(t => t.Id));
    }

    [Fact]
    public void Confirm_HandsBackPickOrderAndFormat_LineTimingsIsTheInverseChip()
    {
        var (lib, _, a, _, solo) = Library();
        var vm = new LyricsStudioPickerViewModel(lib, wordTimings: true, detectFormats: t => t.Select(_ => LyricsFormat.None).ToList());
        LyricsStudioPick? pick = null;
        var closed = 0;
        vm.Confirmed += (_, p) => pick = p;
        vm.CloseRequested += (_, _) => closed++;

        // Nothing ticked: Confirm is a no-op.
        vm.ConfirmCommand.Execute(null);
        Assert.Null(pick);
        Assert.Equal(0, closed);

        vm.SearchText = "lean";
        vm.ToggleSelectCommand.Execute(vm.Results.Single(r => !r.IsAlbum));
        vm.SearchText = "fantas";
        vm.ToggleSelectCommand.Execute(vm.Results.First(r => !r.IsAlbum));
        Assert.Equal("2 songs selected", vm.SelectionText);

        vm.LineTimings = true;
        Assert.False(vm.WordTimings);
        vm.ConfirmCommand.Execute(null);

        Assert.NotNull(pick);
        Assert.Equal(new[] { solo.Id, a.Id }, pick!.Tracks.Select(t => t.Id));
        Assert.False(pick.WordTimings);
        Assert.Equal(1, closed);
    }
}
