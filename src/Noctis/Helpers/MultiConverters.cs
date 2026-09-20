using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctis.Helpers;

/// <summary>Multi-value converters the tile templates need beyond Avalonia's And/Or.</summary>
public static class MultiConverters
{
    /// <summary>True when no input is true — the inverse of <c>BoolConverters.Or</c>.
    /// A Favorites tile's Play glyph shows when neither its album nor its track is playing.</summary>
    public static readonly IMultiValueConverter NoneTrue = new FuncMultiValueConverter<bool, bool>(
        values => !values.Any(v => v));
}
