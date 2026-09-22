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
/// start after process death, or MediaSessionService's own START_STICKY recreating it with
/// a null intent) there is no session to give, and it stops itself.
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
        // Wrap SessionPlayer (the ForwardingPlayer), not Player (the raw ExoPlayer) directly:
        // that is what routes the session's own Next/Previous through the queue instead of
        // ExoPlayer's item list. See PlaybackEngine.SessionPlayer / SessionForwardingPlayer.
        var player = PlaybackEngine.SessionPlayer;
        if (player == null)
        {
            // No engine to give a session for — either a cold start after process death, or
            // Android recreating this START_STICKY service with a null intent. OnGetSession
            // is the only other place StopSelf() lives, but a sticky restart never calls it
            // (nothing has bound or started an action yet), so without this the instance would
            // linger with _session permanently null, and a later StartService from a new
            // Media3AudioPlayer would hit this already-running instance and never build one.
            StopSelf();
            return;
        }

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
