// src/SCMW.Core/Knowledge/FieldSafety.cs
// The second honesty axis, on top of FieldCapability's "can we write it": "is it SAFE to offer?".
// A wrong but well-typed value on a simple field just shows wrong; a wrong value on a reference,
// array, struct or map can break the item. This is deliberately conservative: anything we are not
// sure is a plain value is Unsafe.
namespace SCMW.Core.Knowledge;

/// <summary>Whether offering a field for editing is safe. Safe = a simple value whose worst case
/// is a wrong display/behaviour; Unsafe = structural or unproven.</summary>
public enum EditSafety { Safe, Unsafe }

/// <summary>
/// Safety rules for SCUM item fields, built from a real survey of the game's items.
/// <para><b>Safe</b> when the name is on the known-safe list, or the type is a plain scalar
/// (number, bool, text/string) and the name is not structural.</para>
/// <para><b>Unsafe</b> otherwise — references (object/softobject/class), arrays, structs, maps,
/// sets, meshes/textures/materials; byte/name fields that are not on the known-safe list (an enum
/// or an identifier can change behaviour); and anything unrecognised.</para>
/// </summary>
public static class FieldSafety
{
    /// <summary>Simple scalar fields we have seen on real items and are happy to offer.</summary>
    private static readonly IReadOnlySet<string> KnownSafeNames = new HashSet<string>(
        new[]
        {
            // Inventory size, weight, wetness, health, misc numbers seen on real items.
            "Weight", "GridInventoryRowSpan", "GridInventoryColumnSpan", "MaxWaterWeightRatio",
            "MaxHealth", "MaxHealthRatioAfterReachingBadQuality", "BoundsRadius",
            "MaxRadiationAmountPerWeight", "DefaultDirtiness",
            // Stack size on an item's component object.
            "MaxQuantity", "MaxStackSize",
            // Display text.
            "Caption", "Description",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Names that carry a structural meaning even before the type is looked at.</summary>
    private static readonly string[] StructuralKeywords =
    {
        "mesh", "material", "texture", "class", "blueprint", "object", "struct", "array",
        "visual", "anim", "icon", "preset", "reference", "component", "softobject", "map", "set",
    };

    /// <summary>UE type cores that are plain values. Note: <c>byte</c> and <c>name</c> are NOT here
    /// (an enum-backed byte or an identifier can change behaviour) — they need a known-safe name.</summary>
    private static readonly IReadOnlySet<string> SimpleCores = new HashSet<string>(
        new[]
        {
            "float", "double", "int", "int8", "int16", "int32", "int64",
            "uint8", "uint16", "uint32", "uint64", "short", "ushort", "long", "ulong", "sbyte",
            "bool", "boolean", "str", "string", "text", "char",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Safe only for a known-safe name or a plain scalar type on a non-structural name.</summary>
    public static EditSafety For(string? technicalName, string? valueTypeHint)
    {
        var name = string.IsNullOrWhiteSpace(technicalName) ? null : technicalName.Trim();

        if (IsStructuralName(name))
            return EditSafety.Unsafe;

        if (name is not null && KnownSafeNames.Contains(name))
            return EditSafety.Safe;

        var core = Core(valueTypeHint);
        if (core is not null && SimpleCores.Contains(core))
            return EditSafety.Safe;

        return EditSafety.Unsafe;
    }

    /// <summary>
    /// True when the name carries a structural meaning (a mesh, an array, a reference, …) even
    /// before the type is looked at. Shared with <see cref="ValueSafety"/> so both agree.
    /// </summary>
    internal static bool IsStructuralName(string? technicalName)
    {
        var name = string.IsNullOrWhiteSpace(technicalName) ? null : technicalName.Trim();
        return name is not null &&
               StructuralKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The plain type core of a UE type hint ("FloatProperty" → "float"), or null.</summary>
    internal static string? TypeCore(string? valueTypeHint) => Core(valueTypeHint);

    private static string? Core(string? valueTypeHint)
    {
        if (string.IsNullOrWhiteSpace(valueTypeHint)) return null;

        var hint = valueTypeHint.Trim();
        if (hint.IndexOfAny(new[] { '<', '>', '[', ']', ',', '|' }) >= 0) return "container";

        var core = hint.ToLowerInvariant();
        if (core.EndsWith("property", StringComparison.Ordinal))
            core = core[..^"property".Length].Trim();

        if (core.StartsWith("tarray") || core.StartsWith("tmap") || core.StartsWith("tset"))
            return "container";

        return core;
    }
}
