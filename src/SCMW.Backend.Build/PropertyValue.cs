using System.Globalization;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// Sets one typed UAssetAPI property from a string. Only value kinds we can set correctly are
/// accepted — anything else (objects, arrays, structs) is refused so the whole build fails rather
/// than writing a value the game would ignore.
/// </summary>
internal static class PropertyValue
{
    internal static bool TrySet(PropertyData property, INameMap? names, string raw, out string? error)
    {
        error = null;
        try
        {
            switch (property)
            {
                case FloatPropertyData f:
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                        return NotANumber(raw, out error);
                    f.Value = fv; return true;

                case DoublePropertyData d:
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                        return NotANumber(raw, out error);
                    d.Value = dv; return true;

                case IntPropertyData i:
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        return NotWhole(raw, out error);
                    i.Value = iv; return true;

                case Int64PropertyData l:
                    if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                        return NotWhole(raw, out error);
                    l.Value = lv; return true;

                case BytePropertyData b:
                    // A plain byte is a number; an enum-backed byte keeps its choice in EnumValue
                    // (an FName), not in Value, so each is set its own way.
                    if (b.ByteType != BytePropertyType.Byte)
                    {
                        b.EnumValue = FName.FromString(names!, raw);
                        return true;
                    }
                    if (!byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bv))
                        return NotWhole(raw, out error);
                    b.Value = bv; return true;

                // A full enum property (e.g. _baseElementType) keeps its choice as an FName; the
                // value the screen shows ("EBaseElementType::Well") is exactly what is stored.
                case EnumPropertyData e:
                    e.Value = FName.FromString(names!, raw); return true;

                case BoolPropertyData bo:
                    if (!TryBool(raw, out var bov))
                    {
                        error = $"\"{raw}\" isn't a yes/no value — use true or false.";
                        return false;
                    }
                    bo.Value = bov; return true;

                case StrPropertyData s:
                    s.Value = FString.FromString(raw); return true;

                // A display name like a base element's "_name" is a TextProperty (its Value is an
                // FString, exactly like a StrProperty), so it is set the same way.
                case TextPropertyData t:
                    t.Value = FString.FromString(raw); return true;

                case NamePropertyData n:
                    n.Value = FName.FromString(names!, raw); return true;

                default:
                    error = Unsupported(); return false;
            }
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Setting a value failed ({ex.GetType().Name}).");
            error = "That value couldn't be set.";
            return false;
        }
    }

    private static bool NotANumber(string raw, out string? error)
    {
        error = $"\"{raw}\" is not a number this value can take.";
        return false;
    }

    /// <summary>
    /// A whole-number field was given something it cannot hold. A fraction ("0.5") is a real number —
    /// saying so plainly is friendlier than calling it "not a number".
    /// </summary>
    private static bool NotWhole(string raw, out string? error)
    {
        error = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? $"\"{raw}\" can't be used here — this value has to be a whole number."
            : $"\"{raw}\" is not a number this value can take.";
        return false;
    }

    private static string Unsupported() => "That kind of value cannot be changed by the mod maker yet.";

    private static bool TryBool(string raw, out bool value)
    {
        var text = (raw ?? string.Empty).Trim();
        if (bool.TryParse(text, out value)) return true;
        if (text.Equals("yes", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (text.Equals("no", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        if (text == "1") { value = true; return true; }
        if (text == "0") { value = false; return true; }
        value = false;
        return false;
    }
}
