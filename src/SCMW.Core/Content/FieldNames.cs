// src/SCMW.Core/Content/FieldNames.cs
// Owner-facing field labels. A curated dictionary covers SCUM's real fields; anything
// else gets a deterministic auto-tidy so the Assets detail pane never shows raw
// Unreal-style identifiers. This is a label layer only - it is never an editability
// claim, and callers must keep AssetField.TechnicalName for Plan 3's Technical Mode.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SCMW.Core.Content;

/// <summary>Owner-facing field labels: a curated dictionary for SCUM's real fields, then a
/// deterministic auto-tidy fallback. Labels only - never an editability claim.</summary>
public static class FieldNames
{
    /// <summary>Exact, case-insensitive technical name to friendly label.</summary>
    private static readonly IReadOnlyDictionary<string, string> Known =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GridInventoryRowSpan"] = "Inventory Height",
            ["GridInventoryColumnSpan"] = "Inventory Width",
            ["GridInventoryIcon"] = "Inventory Icon",
            ["MaxStackSize"] = "Max Stack",
            ["MaxQuantity"] = "Max Quantity",
            ["ItemDropSize"] = "Drop Size",
            ["SpawnRate"] = "Spawn Rate",
            ["MaxItems"] = "Max Items",
            ["SpawnerPresets"] = "Spawner Presets",
            ["RequiredSkill"] = "Required Skill",
            ["OutputCount"] = "Output Count",
            ["RecipeName"] = "Recipe Name",
            ["RecipeId"] = "Recipe ID",
            ["SourceAsset"] = "Source Asset",
            ["TableName"] = "Table Name",
            ["AttackSpeed"] = "Attack Speed",
            ["AmmoType"] = "Ammo Type",
            ["DrawTime"] = "Draw Time",
            ["HealAmount"] = "Heal Amount",
            ["WaterCapacity"] = "Water Capacity",
            ["CraftingSkill"] = "Crafting Skill",
            ["CamoPattern"] = "Camo Pattern",
            ["DespawnTime"] = "Despawn Time",
            ["ItemRarity"] = "Item Rarity",
            ["RepairValue"] = "Repair Value",
            ["UseAnimation"] = "Use Animation",
            ["TagName"] = "Tag Name",
            ["ItemType"] = "Item Type",
            ["ItemName"] = "Display Name",
            ["ItemClass"] = "Item Class",
            ["SlotType"] = "Slot Type",
            ["ItemDamage"] = "Damage",
            ["Damage"] = "Damage",
            ["Weight"] = "Weight",
        };

    /// <summary>Unreal asset prefixes stripped before auto-tidying (longest first).</summary>
    private static readonly string[] Prefixes = { "Default__", "BP_", "DA_", "DT_" };

    /// <summary>Known acronyms kept readable instead of being title-cased into "Id" etc.</summary>
    private static readonly IReadOnlyDictionary<string, string> Acronyms =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = "ID",
            ["NPC"] = "NPC",
            ["UI"] = "UI",
        };

    private static readonly Regex LowerToUpper = new("([a-z0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex AcronymToWord = new("([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>
    /// Return the owner-facing label for <paramref name="technicalName"/>. Known fields
    /// map exactly; anything else is auto-tidied. Never returns null, empty or whitespace.
    /// </summary>
    public static string Friendly(string? technicalName)
    {
        if (string.IsNullOrWhiteSpace(technicalName))
        {
            return "Value";
        }

        var name = technicalName.Trim();
        if (Known.TryGetValue(name, out var label))
        {
            return label;
        }

        var tidied = Tidy(name);
        return string.IsNullOrWhiteSpace(tidied) ? "Value" : tidied;
    }

    private static string Tidy(string name)
    {
        var working = name;

        // Strip any run of known Unreal prefixes, then a leading underscore.
        bool stripped;
        do
        {
            stripped = false;
            foreach (var prefix in Prefixes)
            {
                if (working.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    working = working[prefix.Length..];
                    stripped = true;
                    break;
                }
            }
        }
        while (stripped);

        working = working.TrimStart('_');

        // Remaining separators (e.g. DT_ prefixes already stripped, but "DuctTape_C"
        // leaves an inner underscore) read better as word breaks.
        working = working.Replace('_', ' ');

        // Unreal boolean prefix: bAlwaysSpawn -> Always Spawn.
        working = working.TrimStart();
        if (working.Length > 1 && working[0] == 'b' && char.IsUpper(working[1]))
        {
            working = working[1..];
        }

        if (string.IsNullOrWhiteSpace(working))
        {
            return string.Empty;
        }

        // Split camelCase/PascalCase and acronym-to-word boundaries into separate words.
        var spaced = LowerToUpper.Replace(working, "$1 $2");
        spaced = AcronymToWord.Replace(spaced, "$1 $2");

        var words = spaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(Word(word));
        }

        return sb.ToString();
    }

    private static string Word(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        if (Acronyms.TryGetValue(word, out var acronym))
        {
            return acronym;
        }

        if (word.Length == 1)
        {
            return word.ToUpperInvariant();
        }

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }
}
