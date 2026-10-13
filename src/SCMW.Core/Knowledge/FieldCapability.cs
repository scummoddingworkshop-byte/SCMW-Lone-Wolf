// src/SCMW.Core/Knowledge/FieldCapability.cs
// The honesty engine's first half: can we even write this field? Capability is a
// CONSERVATIVE claim about whether a single property value can be changed, judged from
// the field's technical name and (when known) its UE property type. It is used by
// ValueSafety to refuse a field the writer could not handle.
namespace SCMW.Core.Knowledge;

/// <summary>Whether a field's value can be written at all. Conservative by design.</summary>
public enum WriteCapability { Unknown, NotWritable, Writable }

/// <summary>
/// Conservative write-capability rules for SCUM item fields.
///
/// RULE TABLE (first match wins):
/// <list type="number">
/// <item>No usable name and no type hint -> <see cref="WriteCapability.Unknown"/>.</item>
/// <item>Complex type hint (struct/array/map/object/class/mesh/material/texture/...)
///       -> <see cref="WriteCapability.NotWritable"/>, even if the name looks scalar.</item>
/// <item>Structural name (explicit list, or containing a structural keyword such as
///       <c>Mesh</c>, <c>Material</c>, <c>Texture</c>, <c>Class</c>, <c>Icon</c>,
///       <c>Preset</c>, <c>Anim</c>, <c>Visual</c>, <c>Array</c>, <c>Struct</c>,
///       <c>Object</c>) -> <see cref="WriteCapability.NotWritable"/>.</item>
/// <item>Known simple scalar name (integers, floats, booleans, strings/names - e.g.
///       <c>MaxStackSize</c>, <c>Weight</c>, <c>GridInventoryRowSpan</c>,
///       <c>ItemRarity</c>) -> <see cref="WriteCapability.Writable"/>.</item>
/// <item>Unrecognised non-blank name WITH a simple scalar type hint -> <see cref="WriteCapability.Writable"/>
///       (the type is known to be a scalar; the name does not change that).</item>
/// <item>Anything else -> <see cref="WriteCapability.Unknown"/> (honest "not assessed").</item>
/// </list>
/// This is a claim about a value slot only. It never makes a field <c>Editable</c> — it only
/// says whether the writer can set a value of that kind.
/// </summary>
public static class FieldCapability
{
    /// <summary>
    /// Field names known to be simple scalar values in SCUM's data assets. Checked
    /// case-insensitively. Keeping this list explicit (rather than guessing from spelling)
    /// is what makes the writable verdict conservative.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownScalarNames = new HashSet<string>(
        new[]
        {
            // Inventory: stack/size/weight/slot.
            "MaxStackSize", "Weight", "GridInventoryRowSpan", "GridInventoryColumnSpan",
            "ItemDropSize", "SlotType", "MaxItems", "OutputCount",
            // Gameplay numbers.
            "Damage", "ItemDamage", "HealAmount", "Health", "WaterCapacity", "Calories",
            "CraftingSkill", "RequiredSkill", "SpawnRate", "DrawTime", "AttackSpeed",
            "Range", "Durability", "RepairValue", "AmmoType", "DespawnTime",
            // Identity and names.
            "ItemName", "DisplayName", "TagName", "ItemRarity", "ItemType", "CamoPattern",
            "RecipeName", "RecipeId", "TableName",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Names that are structural even without any type hint. Most are also caught by the
    /// keyword scan below; the explicit list documents the intent and protects short names
    /// that carry no keyword.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownStructuralNames = new HashSet<string>(
        new[]
        {
            "StaticMesh", "SkeletalMesh", "Material", "Materials", "BodyTexture", "Texture",
            "Icon", "GridInventoryIcon", "ItemClass", "Visual", "UseAnimation",
            "SpawnerPresets", "ImportData", "AssetImportData", "ThumbnailInfo", "Thumbnail",
        },
        StringComparer.OrdinalIgnoreCase);

    // Substring keywords that mark a name as a structural/complex reference. Kept short and
    // conservative: a false "not writable" is preferable to a false "writable".
    private static readonly string[] StructuralKeywords =
    {
        "mesh", "material", "texture", "class", "blueprint", "object", "struct", "array",
        "visual", "anim", "icon", "preset", "thumbnail", "importdata", "reference",
        "component", "softobject",
    };

    // Exact UE (or plain) type cores that mean "simple scalar value".
    private static readonly IReadOnlySet<string> ScalarTypeCores = new HashSet<string>(
        new[]
        {
            "int", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64",
            "byte", "sbyte", "short", "ushort", "long", "ulong", "float", "double",
            "decimal", "bool", "boolean", "str", "string", "name", "text", "char", "enum",
            "numeric", "number",
        },
        StringComparer.OrdinalIgnoreCase);

    // Exact UE type cores that mean "structural/complex value".
    private static readonly IReadOnlySet<string> ComplexTypeCores = new HashSet<string>(
        new[]
        {
            "struct", "array", "map", "set", "object", "class", "interface", "delegate",
            "multicastdelegate", "softobject", "softclass", "weakobject", "lazypointer",
            "mesh", "staticmesh", "skeletalmesh", "texture", "material", "blueprint",
            "actor", "component",
        },
        StringComparer.OrdinalIgnoreCase);

    private enum HintKind { None, Scalar, Complex }

    /// <summary>
    /// The conservative write capability for a field name and optional UE type hint.
    /// Never throws; <paramref name="technicalName"/> and <paramref name="valueTypeHint"/>
    /// may both be null.
    /// </summary>
    public static WriteCapability For(string technicalName, string? valueTypeHint)
    {
        var hint = ClassifyHint(valueTypeHint);
        var name = string.IsNullOrWhiteSpace(technicalName) ? null : technicalName.Trim();

        // A complex type clue outranks everything: the slot holds something structural.
        if (hint == HintKind.Complex)
            return WriteCapability.NotWritable;

        // Structural names stay not-writable even when handed a scalar hint.
        if (name is not null &&
            (KnownStructuralNames.Contains(name) || ContainsStructuralKeyword(name)))
            return WriteCapability.NotWritable;

        // Known simple scalar names are writable without any type hint.
        if (name is not null && KnownScalarNames.Contains(name))
            return WriteCapability.Writable;

        // An unrecognised (but present) name is writable only when the type is known scalar.
        if (name is not null && hint == HintKind.Scalar)
            return WriteCapability.Writable;

        // Not assessed: no invented capability.
        return WriteCapability.Unknown;
    }

    private static bool ContainsStructuralKeyword(string name) =>
        StructuralKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classify a type hint. A hint may be a bare type ("int"), a UE property type
    /// ("IntProperty"), or a container expression ("TArray&lt;int&gt;"). Anything we do not
    /// recognise is <see cref="HintKind.None"/>, leaving the decision to the name.
    /// </summary>
    private static HintKind ClassifyHint(string? valueTypeHint)
    {
        if (string.IsNullOrWhiteSpace(valueTypeHint))
            return HintKind.None;

        var hint = valueTypeHint.Trim();

        // Container syntax is structural regardless of what it wraps.
        if (hint.IndexOfAny(new[] { '<', '>', '[', ']', ',', '|' }) >= 0)
            return HintKind.Complex;

        var core = hint.ToLowerInvariant();
        if (core.EndsWith("property", StringComparison.Ordinal))
            core = core[..^"property".Length];
        core = core.Trim();

        if (core.StartsWith("tarray", StringComparison.Ordinal) ||
            core.StartsWith("tmap", StringComparison.Ordinal) ||
            core.StartsWith("tset", StringComparison.Ordinal))
            return HintKind.Complex;

        if (ScalarTypeCores.Contains(core) || core is "fname" or "fstring" or "ftext")
            return HintKind.Scalar;

        if (ComplexTypeCores.Contains(core))
            return HintKind.Complex;

        // Descriptive type names such as "StaticMeshReference" or "Texture2D".
        if (core.Contains("texture", StringComparison.Ordinal) ||
            core.Contains("mesh", StringComparison.Ordinal) ||
            core.Contains("material", StringComparison.Ordinal) ||
            core.Contains("blueprint", StringComparison.Ordinal))
            return HintKind.Complex;

        return HintKind.None;
    }
}
