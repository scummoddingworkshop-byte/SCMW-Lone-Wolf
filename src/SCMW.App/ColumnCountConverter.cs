using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SCMW.App;

/// <summary>
/// How many project cards fit in a row: three at a normal window width, and more only once the
/// window is really wide — so the cards keep a sensible shape and names have room to read.
/// </summary>
public sealed class ColumnCountConverter : IValueConverter
{
    /// <summary>The width one card wants to itself.</summary>
    private const double WidthPerColumn = 380;

    private const int Least = 3;
    private const int Most = 6;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0) return Least;

        var (perColumn, least, most) = Settings(parameter);
        return Math.Clamp((int)(width / perColumn), least, most);
    }

    /// <summary>The column rules for one list, given as "width each, fewest, most" — so a page can
    /// say how wide its own cards want to be instead of inheriting the project tiles' numbers.</summary>
    private static (double PerColumn, int Least, int Most) Settings(object? parameter)
    {
        if (parameter is not string text || string.IsNullOrWhiteSpace(text))
            return (WidthPerColumn, Least, Most);

        var parts = text.Split(',');
        var perColumn = parts.Length > 0 && double.TryParse(parts[0], out var width) ? width : WidthPerColumn;
        var least = parts.Length > 1 && int.TryParse(parts[1], out var fewest) ? fewest : Least;
        var most = parts.Length > 2 && int.TryParse(parts[2], out var many) ? many : Most;
        return (perColumn, least, most);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
