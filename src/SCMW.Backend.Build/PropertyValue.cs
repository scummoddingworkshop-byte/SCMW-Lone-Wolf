using System.Globalization;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// Sets one typed UAssetAPI property from a string. Only value kinds we can set correctly are
/// accepted — anything else (including enum-backed bytes, objects, arrays, structs) is refused so
/// the whole build fails rather than writing a value the game would ignore.
/// </summary>
internal static class PropertyValue
{
    internal static bool TrySet(PropertyData property, UAsset? asset, string raw, out string? error)
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
                        return NotANumber(raw, out error);
                    i.Value = iv; return true;

                case Int64PropertyData l:
                    if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                        return NotANumber(raw, out error);
                    l.Value = lv; return true;

                case BytePropertyData b:
                    // An enum value lives in EnumValue (an FName); the byte Value is not serialized,
                    // so setting it would silently do nothing. Refuse rather than lie.
                    if (b.ByteType != BytePropertyType.Byte)
                        { error = Unsupported(); return false; }
                    if (!byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bv))
                        return NotANumber(raw, out error);
                    b.Value = bv; return true;

                case BoolPropertyData bo:
                    if (!TryBool(raw, out var bov)) return NotANumber(raw, out error);
                    bo.Value = bov; return true;

                case StrPropertyData s:
                    s.Value = FString.FromString(raw); return true;

                case NamePropertyData n:
                    n.Value = FName.FromString(asset!, raw); return true;

                default:
                    error = Unsupported(); return false;
            }
        }
        catch (Exception ex)
        {
            error = "That value could not be set (" + ex.GetType().Name + ").";
            return false;
        }
    }

    private static bool NotANumber(string raw, out string? error)
    {
        error = $"\"{raw}\" is not a number this value can take.";
        return false;
    }

    private static string Unsupported() => "That kind of value cannot be changed by the mod maker yet.";

    private static bool TryBool(string raw, out bool value)
    {
        if (bool.TryParse(raw, out value)) return true;
        if (raw.Trim() == "1") { value = true; return true; }
        if (raw.Trim() == "0") { value = false; return true; }
        value = false;
        return false;
    }
}
