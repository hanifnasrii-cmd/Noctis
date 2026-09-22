using Avalonia.Media;

namespace Noctis.Helpers;

/// <summary>
/// GitHub #74: the colour of a user badge pill. Picked from a fixed palette by the
/// badge's name (case-insensitive), so "Gym" is the same colour in every playlist and
/// after every restart without a colour picker or any stored mapping.
/// </summary>
public static class BadgePalette
{
    private static readonly Color[] Colors =
    {
        Color.Parse("#E5484D"), // red
        Color.Parse("#F76B15"), // orange
        Color.Parse("#FFC53D"), // amber
        Color.Parse("#30A46C"), // green
        Color.Parse("#12A594"), // teal
        Color.Parse("#0090FF"), // blue
        Color.Parse("#6E56CF"), // violet
        Color.Parse("#E93D82"), // pink
        Color.Parse("#8E4EC6"), // purple
        Color.Parse("#00A2C7"), // cyan
    };

    public static Color ColorFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Colors[0];
        // Stable across processes (string.GetHashCode is randomised per run).
        var hash = 17u;
        foreach (var ch in name.Trim().ToUpperInvariant())
            hash = hash * 31 + ch;
        return Colors[(int)(hash % (uint)Colors.Length)];
    }

    public static IBrush BrushFor(string? name) => new SolidColorBrush(ColorFor(name));
}
