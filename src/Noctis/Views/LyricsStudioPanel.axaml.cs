using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctis.ViewModels;

namespace Noctis.Views;

/// <summary>
/// The Lyrics Studio surface shared by the per-song dialog and the sidebar page. Owns the
/// tap-mode keys (Space stamps the next word, Esc leaves) and, when the host set none, a
/// confirmation prompt over the main window.
/// </summary>
public partial class LyricsStudioPanel : UserControl
{
    /// <summary>Title, subtitle and the round X. The sidebar page hides it and draws its own header.</summary>
    public static readonly StyledProperty<bool> ShowHeaderProperty =
        AvaloniaProperty.Register<LyricsStudioPanel, bool>(nameof(ShowHeader), true);

    public bool ShowHeader
    {
        get => GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public LyricsStudioPanel()
    {
        InitializeComponent();

        // Tunnelled so a focused Button cannot swallow Space first; typing in a TextBox is left alone.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not LyricsStudioViewModel m || !m.IsTapping) return;
            if (e.Source is TextBox) return;
            if (e.Key == Key.Space) { m.TapCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape) { m.CancelTapCommand.Execute(null); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (DataContext is LyricsStudioViewModel { IsTapping: true } && e.Key == Key.Space && e.Source is not TextBox) e.Handled = true;
        }, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            // The dialog sets Confirm over itself before the panel attaches; the page has no
            // window of its own, so the prompt goes over the main window.
            if (DataContext is LyricsStudioViewModel { Confirm: null } vm)
                vm.Confirm = message => ConfirmationDialog.ShowAsync(message);
        };
    }
}
