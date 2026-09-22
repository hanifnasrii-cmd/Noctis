using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Media3.Session;

namespace Noctis.Android.Services;

/// <summary>
/// Hosts the MediaSession for the shared ExoPlayer. Media3 turns this into a foreground
/// service with the standard media notification (title, artist, artwork from the
/// MediaItem metadata, play/pause/skip) whenever the player is playing, and routes
/// lock-screen, Bluetooth and headset button events to the player. Started by
/// Media3AudioPlayer on the first Play; when Android restarts it without an engine (cold
/// start after process death) there is no session to give, and it stops itself.
/// </summary>
[Service(Name = "com.heartached.noctis.PlaybackService", Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(new[] { "androidx.media3.session.MediaSessionService" })]
public sealed class NoctisPlaybackService : MediaSessionService
{
    private MediaSession? _session;

    public override void OnCreate()
    {
        base.OnCreate();
        var player = PlaybackEngine.Player;
        if (player == null) return;

        // Tapping the notification brings the (single-task) activity back.
        var launch = new Intent(this, typeof(MainActivity));
        var sessionActivity = PendingIntent.GetActivity(this, 0, launch,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        _session = new MediaSession.Builder(this, player)
            .SetSessionActivity(sessionActivity)
            .Build();
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo controllerInfo)
    {
        if (_session == null) StopSelf();
        return _session;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Swiped away from recents: keep playing if we are, otherwise go away with the task.
        var player = _session?.Player;
        if (player == null || !player.PlayWhenReady || player.MediaItemCount == 0)
            StopSelf();
    }

    public override void OnDestroy()
    {
        _session?.Release();
        _session = null;
        base.OnDestroy();
    }
}
