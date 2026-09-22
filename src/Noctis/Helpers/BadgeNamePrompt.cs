using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Noctis.Models;
using Noctis.Views;

namespace Noctis.Helpers;

/// <summary>Parameter of the Badge ▸ menu: the clicked row and the badge to apply
/// (null = remove, <see cref="NewBadge"/> = prompt for a new name).</summary>
public sealed record BadgeRequest(Track Track, string? Badge)
{
    public const string NewBadge = "\u0001new";
}

/// <summary>GitHub #74: opens <see cref="BadgeNameDialog"/> over the main window.</summary>
public static class BadgeNamePrompt
{
    public static async Task<string?> ShowAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not Window owner)
            return null;

        var dialog = new BadgeNameDialog();
        DialogHelper.SizeToOwner(dialog, owner);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }
}
