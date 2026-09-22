using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Noctis.Mobile.Views;

/// <summary>Transport glyphs until Phase 4 brings the icon set over.</summary>
public static class Glyphs
{
    public static readonly IValueConverter PlayPause =
        new FuncValueConverter<bool, string>(playing => playing ? "⏸" : "▶");
    public static readonly IValueConverter Repeat =
        new FuncValueConverter<Noctis.Models.RepeatMode, string>(m => m switch
        {
            Noctis.Models.RepeatMode.All => "🔁",
            Noctis.Models.RepeatMode.One => "🔂",
            _ => "↻",
        });
    public static readonly IValueConverter ShuffleGlyph =
        new FuncValueConverter<bool, string>(on => on ? "🔀" : "→");
}

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
    }
}
