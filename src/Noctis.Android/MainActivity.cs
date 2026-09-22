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

        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri != null)
        {
            // Without this the grant dies with the activity and the next launch's scan
            // finds an unreadable root.
            ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        _pickFolder?.TrySetResult(uri?.ToString());
        _pickFolder = null;
    }
}
