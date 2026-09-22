using System.Globalization;
using Avalonia.Data.Converters;
using Noctis.Helpers;

namespace Noctis.Converters;

/// <summary>GitHub #74: badge name → its palette brush (see <see cref="BadgePalette"/>).</summary>
public sealed class BadgeBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BadgePalette.BrushFor(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
