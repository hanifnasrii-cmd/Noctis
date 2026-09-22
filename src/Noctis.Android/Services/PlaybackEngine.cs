using AndroidX.Media3.ExoPlayer;

namespace Noctis.Android.Services;

/// <summary>
/// The one ExoPlayer, created by <see cref="Media3AudioPlayer"/> on the main thread and
/// wrapped by <see cref="NoctisPlaybackService"/>'s MediaSession. A static because the
/// service is instantiated by Android, not by our DI container.
/// </summary>
internal static class PlaybackEngine
{
    public static IExoPlayer? Player { get; set; }
}
