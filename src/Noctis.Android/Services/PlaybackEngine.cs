using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;

namespace Noctis.Android.Services;

/// <summary>
/// The one ExoPlayer, created by <see cref="Media3AudioPlayer"/> on the main thread. A static
/// because the service is instantiated by Android, not by our DI container.
/// </summary>
internal static class PlaybackEngine
{
    public static IExoPlayer? Player { get; set; }

    /// <summary>
    /// The forwarding player <see cref="NoctisPlaybackService"/>'s MediaSession is built
    /// around, instead of <see cref="Player"/> directly, so the session's own Next/Previous
    /// commands are intercepted and routed through the queue rather than seeking ExoPlayer's
    /// item list on their own. See Media3AudioPlayer.SessionForwardingPlayer.
    /// </summary>
    public static IPlayer? SessionPlayer { get; set; }
}
