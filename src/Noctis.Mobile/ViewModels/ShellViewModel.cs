using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctis.Models;

namespace Noctis.Mobile.ViewModels;

/// <summary>Root of the phone UI: the Library page, the Now Playing / Queue overlays and the mini bar.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(LibraryViewModel library, NowPlayingViewModel player)
    {
        Library = library;
        Player = player;
    }

    public LibraryViewModel Library { get; }
    public NowPlayingViewModel Player { get; }

    [ObservableProperty] private bool _isNowPlayingOpen;
    [ObservableProperty] private bool _isQueueOpen;

    [RelayCommand] private void OpenNowPlaying() => IsNowPlayingOpen = true;

    [RelayCommand]
    private void CloseNowPlaying()
    {
        // The Queue sits above Now Playing, so closing the page must take it down too;
        // otherwise the Queue overlay is left floating over the Library page.
        IsQueueOpen = false;
        IsNowPlayingOpen = false;
    }

    [RelayCommand] private void ToggleQueue() => IsQueueOpen = !IsQueueOpen;

    /// <summary>Tap on a song row: play the song list from that row.</summary>
    [RelayCommand]
    private void PlaySong(Track? track)
    {
        if (track == null) return;
        var songs = Library.Songs.ToList();
        var index = songs.IndexOf(track);
        if (index < 0) return;
        Player.PlayTracks(songs, index);
    }

    public async Task InitializeAsync()
    {
        await Library.InitializeAsync();
        await Player.RestoreStateAsync();
    }

    public Task SaveStateAsync() => Player.SaveStateAsync();
}
