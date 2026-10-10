// src/SCMW.Core/Content/FieldKnowledge.cs
// Presentation-only knowledge about an item field: which friendly group it belongs
// under, whether it is engine bookkeeping the owner never needs to see, and how to
// render its raw value plainly. This is a label/presentation layer ONLY - it never
// changes a field's status or editability, and callers must keep AssetField.TechnicalName
// (Plan 3's Technical Mode). IsInternalNoise judges names alone and is deliberately
// conservative; the caller must also keep any field whose StatusKind is not
// Unknown/ReadOnly (Review Focus 4).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SCMW.Core.Content;

/// <summary>Friendly section an item field is presented under.</summary>
public enum FieldGroup { Inventory, Gameplay, Appearance, ItemSettings, Other }

/// <summary>
/// Field presentation knowledge: friendly groups, the curated list of engine
/// bookkeeping names to hide, and plain value formatting. Never an editability claim.
/// </summary>
public static class FieldKnowledge
{
    /// <summary>
    /// Exact, case-insensitive mappings for SCUM's real fields. Checked before the
    /// keyword heuristics so a field like "GridInventoryIcon" stays Inventory even
    /// though "Icon" alone would otherwise read as Appearance.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, FieldGroup> Known =
        new Dictionary<string, FieldGroup>(StringComparer.OrdinalIgnoreCase)
        {
            // Inventory: span/height/width/icon, stack sizes, weight, drop size, slot.
            ["GridInventoryRowSpan"] = FieldGroup.Inventory,
            ["GridInventoryColumnSpan"] = FieldGroup.Inventory,
            ["GridInventoryIcon"] = FieldGroup.Inventory,
            ["MaxStackSize"] = FieldGroup.Inventory,
            ["ItemDropSize"] = FieldGroup.Inventory,
            ["Weight"] = FieldGroup.Inventory,
            ["SlotType"] = FieldGroup.Inventory,

            // Gameplay: damage, healing, water, calories, crafting, skills, spawn, ammo, speed.
            ["SpawnRate"] = FieldGroup.Gameplay,
            ["MaxItems"] = FieldGroup.Gameplay,
            ["SpawnerPresets"] = FieldGroup.Gameplay,
            ["RequiredSkill"] = FieldGroup.Gameplay,
            ["OutputCount"] = FieldGroup.Gameplay,
            ["RecipeName"] = FieldGroup.Gameplay,
            ["RecipeId"] = FieldGroup.Gameplay,
            ["AttackSpeed"] = FieldGroup.Gameplay,
            ["AmmoType"] = FieldGroup.Gameplay,
            ["DrawTime"] = FieldGroup.Gameplay,
            ["HealAmount"] = FieldGroup.Gameplay,
            ["WaterCapacity"] = FieldGroup.Gameplay,
            ["CraftingSkill"] = FieldGroup.Gameplay,
            ["RepairValue"] = FieldGroup.Gameplay,
            ["ItemDamage"] = FieldGroup.Gameplay,
            ["Damage"] = FieldGroup.Gameplay,

            // Appearance: visuals (camo, animation).
            ["CamoPattern"] = FieldGroup.Appearance,
            ["UseAnimation"] = FieldGroup.Appearance,

            // Item Settings: identity and lifecycle metadata.
            ["ItemName"] = FieldGroup.ItemSettings,
            ["ItemClass"] = FieldGroup.ItemSettings,
            ["ItemType"] = FieldGroup.ItemSettings,
            ["ItemRarity"] = FieldGroup.ItemSettings,
            ["TagName"] = FieldGroup.ItemSettings,
            ["DespawnTime"] = FieldGroup.ItemSettings,
            ["TableName"] = FieldGroup.ItemSettings,
        };

    // Keyword heuristics catch names not in the dictionary. Group order matters: a
    // name is checked Inventory, then Gameplay, then Appearance, then Item Settings.
    private static readonly string[] InventoryKeywords =
        { "Inventory", "Stack", "Weight", "DropSize", "Grid", "Slot", "Container" };

    private static readonly string[] GameplayKeywords =
    {
        "Damage", "Heal", "Health", "Water", "Calorie", "Craft", "Skill", "Spawn",
        "Ammo", "Draw", "Attack", "Range", "Durab", "Repair",
    };

    private static readonly string[] AppearanceKeywords =
    {
        "Icon", "Model", "Mesh", "Material", "Texture", "Camo", "Color", "Colour",
        "Visual", "Anim",
    };

    private static readonly string[] ItemSettingsKeywords =
        { "Name", "Display", "Class", "Type", "Rarity", "Tag", "Despawn" };

    /// <summary>
    /// Curated, deliberately small list of engine bookkeeping names that carry no
    /// owner meaning (import data, thumbnail info, actor tick/replication flags and
    /// editor-only metadata). Kept exact (not substring) so we prefer leaving a field
    /// visible over hiding it. Callers must ALSO keep any field whose status is not
    /// Unknown/ReadOnly - this list is never the only guard.
    /// </summary>
    private static readonly IReadOnlySet<string> InternalNoise = new HashSet<string>(
        new[]
        {
            "ImportData",
            "AssetImportData",
            "ImportPath",
            "ExportPath",
            "ThumbnailInfo",
            "Thumbnail",
            "bActorTickEnabled",
            "PrimaryActorTick",
            "ActorTick",
            "bCanEverTick",
            "bReplicates",
            "bNetLoadOnClient",
            "bAlwaysRelevant",
            "bIsEditorOnlyActor",
            "bIsEditorOnly",
            "EditorOnly",
            "bHiddenEd",
            "SourceFileTimestamp",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Overall string cap for a formatted list before an ellipsis is added.</summary>
    private const int MaxListLength = 80;

    /// <summary>
    /// The friendly group a field belongs under. Explicit SCUM names win, then keyword
    /// heuristics; anything unrecognised is <see cref="FieldGroup.Other"/> (always shown).
    /// </summary>
    public static FieldGroup GroupFor(string? technicalName)
    {
        if (string.IsNullOrWhiteSpace(technicalName))
        {
            return FieldGroup.Other;
        }

        var name = technicalName.Trim();
        if (Known.TryGetValue(name, out var known))
        {
            return known;
        }

        if (MatchesAny(name, InventoryKeywords))
        {
            return FieldGroup.Inventory;
        }

        if (MatchesAny(name, GameplayKeywords))
        {
            return FieldGroup.Gameplay;
        }

        if (MatchesAny(name, AppearanceKeywords))
        {
            return FieldGroup.Appearance;
        }

        if (MatchesAny(name, ItemSettingsKeywords))
        {
            return FieldGroup.ItemSettings;
        }

        return FieldGroup.Other;
    }

    /// <summary>
    /// True when <paramref name="technicalName"/> is on the curated engine-bookkeeping
    /// list. Judges the name only; callers must not hide a field whose status is not
    /// Unknown/ReadOnly (see the class remarks).
    /// </summary>
    public static bool IsInternalNoise(string? technicalName) =>
        !string.IsNullOrWhiteSpace(technicalName) && InternalNoise.Contains(technicalName.Trim());

    /// <summary>
    /// Render a raw field value plainly: blank becomes "-", booleans become Yes/No,
    /// numbers use invariant culture, asset paths become their last segment without
    /// extension, lists are comma-joined and truncated, and anything else falls back to
    /// a safe ToString(). Never throws and never returns a raw null/blank.
    /// </summary>
    public static string FormatValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "-";
        }

        var text = raw.Trim();

        // The engine's own "no value" literal is shown like any other missing value.
        if (string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        if (bool.TryParse(text, out var flag))
        {
            return flag ? "Yes" : "No";
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (text.Contains('/'))
        {
            return LastPathSegment(text);
        }

        if (text.StartsWith('[') || text.StartsWith('(') || text.Contains(','))
        {
            return FormatList(text);
        }

        return text;
    }

    /// <summary>
    /// Object-friendly overload for callers that hold a boxed value: the ToString() is
    /// wrapped so a throwing implementation degrades to "-" instead of crashing.
    /// </summary>
    public static string FormatValue(object? raw)
    {
        if (raw is null)
        {
            return "-";
        }

        string text;
        try
        {
            text = raw.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            // A value we cannot even describe is honestly shown as missing.
            return "-";
        }

        return FormatValue(text);
    }

    private static bool MatchesAny(string name, string[] keywords) =>
        keywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Last path segment with any extension stripped, quotes removed.</summary>
    private static string LastPathSegment(string text)
    {
        var trimmed = text.Trim().Trim('\'', '"');
        var slash = trimmed.LastIndexOf('/');
        var tail = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;

        var dot = tail.LastIndexOf('.');
        if (dot > 0)
        {
            tail = tail[..dot];
        }

        return tail.Length == 0 ? "-" : tail;
    }

    private static string FormatList(string text)
    {
        var inner = text.Trim();
        if (inner.Length >= 2
            && ((inner[0] == '[' && inner[^1] == ']') || (inner[0] == '(' && inner[^1] == ')')))
        {
            inner = inner[1..^1];
        }

        var joined = string.Join(", ", inner
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0));

        if (joined.Length == 0)
        {
            return "-";
        }

        return joined.Length > MaxListLength ? joined[..(MaxListLength - 1)] + "…" : joined;
    }
}
