using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SCMW.App;

/// <summary>
/// Multiplies a number by a factor given as the converter parameter — used to keep the Home hero's
/// shape (its height follows its width), so the artwork's baked-in title always fits.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double number) return null;

        var factor = parameter is string text &&
                     double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1.0;

        return number * factor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
