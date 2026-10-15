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

        return Math.Clamp((int)(width / WidthPerColumn), Least, Most);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
