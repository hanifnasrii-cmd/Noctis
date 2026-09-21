using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Noctis.Android.Views;

namespace Noctis.Android;

public partial class AndroidApp : Avalonia.Application
{
    private ResourceInclude? _activeThemeOverlay;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Merge one of the shared theme overlays (Dark, Midnight, Ink, Smoke) on top of the
    /// base styles, replacing the previous one. The same mechanism as the desktop
    /// App.SetThemeCore; "Gray" is the base look with no overlay.
    /// </summary>
    public void SetTheme(string themeName)
    {
        if (_activeThemeOverlay != null)
        {
            Resources.MergedDictionaries.Remove(_activeThemeOverlay);
            _activeThemeOverlay = null;
        }

        var file = themeName switch
        {
            "Dark" => "Dark",
            "Midnight" => "Midnight",
            "Ink" => "Ink",
            "Smoke" => "Smoke",
            _ => null,
        };
        if (file == null) return;

        _activeThemeOverlay = new ResourceInclude(new Uri("avares://Noctis.UI/"))
        {
            Source = new Uri($"avares://Noctis.UI/Assets/Themes/{file}.axaml"),
        };
        Resources.MergedDictionaries.Add(_activeThemeOverlay);
    }
}
