using System;
using System.Globalization;

namespace SCMW.Core.Editing;

/// <summary>
/// Reads the plain-language factor a player types for a multiply — "×2", "2", "half", "double",
/// "+50%", "-25%" — and works it out as a number, so the words on screen and the number in the change
/// agree and cannot drift. A percentage always means "this much more" (or less): "+50%" is ×1.5 and
/// "-25%" is ×0.75, with or without the sign written.
/// </summary>
public static class Multiplier
{
    /// <summary>Read a factor from what the player typed. False when it is not a factor we can use.</summary>
    public static bool TryParse(string? text, out double factor)
    {
        factor = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();

        // Friendly words first.
        switch (trimmed.ToLowerInvariant())
        {
            case "half": factor = 0.5; return true;
            case "double": factor = 2; return true;
            case "triple": factor = 3; return true;
        }

        // A percentage means "this much more/less": "+50%" -> 1.5, "-25%" -> 0.75, "50%" -> 1.5.
        if (trimmed.EndsWith('%'))
        {
            var body = trimmed[..^1].Trim();
            var sign = 1.0;
            if (body.StartsWith('+')) body = body[1..];
            else if (body.StartsWith('-')) { sign = -1.0; body = body[1..]; }

            if (!TryReadNumber(body, out var percent)) return false;

            var candidate = 1.0 + (sign * percent / 100.0);
            if (!double.IsFinite(candidate)) return false;
            factor = candidate;
            return true;
        }

        // A leading × or x is just decoration ("×2" is the same as "2").
        if (trimmed.StartsWith('×') || trimmed.StartsWith('x') || trimmed.StartsWith('X'))
        {
            trimmed = trimmed[1..].Trim();
        }

        if (!TryReadNumber(trimmed, out var value)) return false;
        factor = value;
        return true;
    }

    /// <summary>
    /// Apply a factor to a field's game value. A value we cannot read as a number is returned exactly
    /// as it was (never guessed), and a result too large to show is refused the same way. The result is
    /// a bare number — the field's unit stays where it is, on the row.
    /// </summary>
    public static string? Apply(string? gameValue, double factor)
    {
        if (!TryReadNumber(gameValue, out var baseValue)) return gameValue;

        var product = baseValue * factor;
        if (!double.IsFinite(product)) return gameValue;

        // A product that lands on zero is shown as a clean "0" (never "-0").
        return product == 0 ? "0" : product.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryReadNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }
}
