using System.Linq;
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
    /// <summary>Child lookups for <see cref="OnTitleCellLayoutUpdated"/>, resolved once per cell and
    /// stashed in Tag: LayoutUpdated fires after every layout pass and a cell's children never change.</summary>
    private sealed record TitleCellChildren(Control Title, Control? ExplicitBadge);

    /// <summary>
    /// A title cell is an Auto,Auto grid so the E badge hugs the title; Auto columns measure
    /// unbounded, so the title's MaxWidth is capped to the cell minus the badge here and
    /// TextTrimming does the rest (the AddSongsDialog recipe).
    /// </summary>
    private void OnTitleCellLayoutUpdated(object? sender, System.EventArgs e)
    {
        if (sender is not Grid cell) return;
        if (cell.Tag is not TitleCellChildren children)
        {
            var title = cell.Children.FirstOrDefault(c => c.Name == "TitleBox");
            if (title is null) return;
            children = new TitleCellChildren(title, cell.Children.FirstOrDefault(c => c.Name == "ExplicitBadge"));
            cell.Tag = children;
        }

        var reserved = 0.0;
        if (children.ExplicitBadge is { IsVisible: true } badge)
        {
            var width = badge.Bounds.Width > 0 ? badge.Bounds.Width : badge.DesiredSize.Width;
            reserved = width + badge.Margin.Left + badge.Margin.Right;
        }
        var max = System.Math.Max(0, cell.Bounds.Width - reserved);
        if (System.Math.Abs(children.Title.MaxWidth - max) > 0.5)
            children.Title.MaxWidth = max;
    }

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
