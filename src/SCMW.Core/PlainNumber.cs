using System.Globalization;

namespace SCMW.Core;

/// <summary>
/// The plain number the app shows for a value. A whole number stays as it is; a number with a
/// fraction is shown with three places after the dot, so float noise (0.15000000000000002) never
/// reaches the owner's eyes. Display only — the file keeps, and the build writes, the real value.
/// </summary>
public static class PlainNumber
{
    /// <summary>The value as the owner should read it. Never throws, never null.</summary>
    public static string ForDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

        var text = raw.Trim();
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c) && c != '.' && c != '-' && c != '+') return raw;   // a path, a word, None
        }

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return raw;

        var whole = decimal.Truncate(value);
        if (value == whole)
            return whole.ToString("0", CultureInfo.InvariantCulture);

        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
