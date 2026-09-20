using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctis.Converters;

/// <summary>
/// Bar-fill width: a 0..1 fraction of the track's actual laid-out width. Replaces the
/// old fixed-pixel maximum (400px), which left every 100% bar stopping at 400px inside
/// a track that was wider than that (Discord report, Statistics › Most Skipped, 2026-09-07).
/// Inputs: [fraction, trackWidth].
/// </summary>
public class FractionOfWidthConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not double fraction || values[1] is not double width
            || double.IsNaN(width) || double.IsInfinity(width))
            return 0.0;
        return Math.Max(0, Math.Min(1, fraction)) * Math.Max(0, width);
    }
}
