using Android.App;
using Android.Content.PM;
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
}
