using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Noctis.Helpers;

/// <summary>
/// Resolves a StreamGeometry resource key ("SettingsIcon") from Assets/Icons.axaml to the
/// geometry itself, so a data template can draw an icon named by its view model
/// (the Settings rail). Unknown keys yield null and the PathIcon draws nothing.
/// </summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static readonly IconKeyConverter Instance = new();

    /// <summary>The sidebar's bitmap icons (PNG opacity masks), for rail keys that are not geometries.</summary>
    public static readonly Noctis.Converters.IconKeyToGeometryConverter Bitmap = new();

    /// <summary>True when the key is one of the bitmap sidebar icons rather than a geometry.</summary>
    public static readonly FuncValueConverter<string?, bool> IsBitmap = new(Noctis.Converters.IconKeyToGeometryConverter.HasKey);

    /// <summary>The inverse: the key names a StreamGeometry drawn with a PathIcon (sidebar + rail).</summary>
    public static readonly FuncValueConverter<string?, bool> IsGeometry = new(k => !Noctis.Converters.IconKeyToGeometryConverter.HasKey(k));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0) return null;
        return Application.Current?.TryGetResource(key, null, out var res) == true ? res as Geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
