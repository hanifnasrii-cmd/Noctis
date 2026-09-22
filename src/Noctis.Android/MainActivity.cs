using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace Noctis.Android;

[Activity(
    Label = "Noctis",
    Theme = "@style/NoctisTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int PickFolderRequest = 4242;

    /// <summary>The live activity, for services that need to start system UI (the SAF picker).</summary>
    public static MainActivity? Current { get; private set; }

    private TaskCompletionSource<string?>? _pickFolder;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnPause()
    {
        base.OnPause();
        // Screen off / home / task switch: checkpoint the queue and position now, since
        // Android may kill the process without any further callback.
        AndroidApp.Current?.OnBackgrounded();
    }

    protected override void OnDestroy()
    {
        // A config change or low-memory kill while the system picker is foreground (our
        // ConfigurationChanges only covers orientation/screen size/UI mode; a locale or
        // density change still recreates us) recreates the activity before
        // OnActivityResult fires. That callback lands on the new instance, where
        // _pickFolder is null, so the old completion source would otherwise be abandoned
        // unresolved and the caller (LibraryViewModel.AddFolderAsync, an AsyncRelayCommand
        // that disallows concurrent execution) would stay stuck "running" forever.
        // Resolving with null here — before Current is cleared — keeps that button usable.
        _pickFolder?.TrySetResult(null);
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }

    /// <summary>Shows the system document-tree picker; resolves to the persisted tree URI or null.</summary>
    public Task<string?> PickFolderAsync()
    {
        _pickFolder?.TrySetResult(null);
        _pickFolder = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickFolderRequest);
        return _pickFolder.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickFolderRequest) return;

        // Take the field before doing anything else: if the activity was recreated mid-pick,
        // OnDestroy already resolved (and nulled) the old completion source, and this result
        // is landing on a fresh instance that never issued the request. In that case there is
        // no one left to hand the URI to, so the grant below must not be taken either.
        var pending = _pickFolder;
        _pickFolder = null;
        if (pending == null) return;

        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri != null)
        {
            // Only taken when a URI is actually about to be returned: Android caps persisted
            // grants per app (128 pre-12, 512 on 12+), and taking one for a result that gets
            // discarded (see above) would slowly burn slots with nothing referencing them.
            // Without this the grant dies with the activity and the next launch's scan
            // finds an unreadable root.
            ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        pending.TrySetResult(uri?.ToString());
    }
}
