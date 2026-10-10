// tests/SCMW.Core.Tests/FieldNamesTests.cs
// Task E: owner-facing field labels. The dictionary must map SCUM's real fields
// exactly; everything else is auto-tidied deterministically. The fallback must
// never return an empty/whitespace label for a non-empty input.
using System.Collections.Generic;
using SCMW.Core.Content;
using Xunit;

namespace SCMW.Core.Tests;

public class FieldNamesTests
{
    [Theory]
    [InlineData("GridInventoryRowSpan", "Inventory Height")]
    [InlineData("GridInventoryColumnSpan", "Inventory Width")]
    [InlineData("GridInventoryIcon", "Inventory Icon")]
    [InlineData("MaxStackSize", "Max Stack")]
    [InlineData("ItemDropSize", "Drop Size")]
    [InlineData("SpawnRate", "Spawn Rate")]
    [InlineData("MaxItems", "Max Items")]
    [InlineData("SpawnerPresets", "Spawner Presets")]
    [InlineData("RequiredSkill", "Required Skill")]
    [InlineData("OutputCount", "Output Count")]
    [InlineData("RecipeName", "Recipe Name")]
    [InlineData("RecipeId", "Recipe ID")]
    [InlineData("SourceAsset", "Source Asset")]
    [InlineData("TableName", "Table Name")]
    [InlineData("AttackSpeed", "Attack Speed")]
    [InlineData("AmmoType", "Ammo Type")]
    [InlineData("DrawTime", "Draw Time")]
    [InlineData("HealAmount", "Heal Amount")]
    [InlineData("WaterCapacity", "Water Capacity")]
    [InlineData("CraftingSkill", "Crafting Skill")]
    [InlineData("CamoPattern", "Camo Pattern")]
    [InlineData("DespawnTime", "Despawn Time")]
    [InlineData("ItemRarity", "Item Rarity")]
    [InlineData("RepairValue", "Repair Value")]
    [InlineData("UseAnimation", "Use Animation")]
    [InlineData("TagName", "Tag Name")]
    [InlineData("ItemType", "Item Type")]
    [InlineData("ItemName", "Display Name")]
    [InlineData("ItemClass", "Item Class")]
    [InlineData("SlotType", "Slot Type")]
    [InlineData("ItemDamage", "Damage")]
    [InlineData("Damage", "Damage")]
    [InlineData("Weight", "Weight")]
    public void Known_field_maps_to_its_exact_label(string technical, string expected)
    {
        Assert.Equal(expected, FieldNames.Friendly(technical));
    }

    [Theory]
    [InlineData("gridinventoryrowspan", "Inventory Height")] // case-insensitive match
    [InlineData("MAXSTACKSIZE", "Max Stack")]
    public void Known_field_match_ignores_case(string technical, string expected)
    {
        Assert.Equal(expected, FieldNames.Friendly(technical));
    }

    [Theory]
    [InlineData("SomeUnknownThing", "Some Unknown Thing")]
    [InlineData("NPCSpawnerId", "NPC Spawner ID")]
    [InlineData("bAlwaysSpawn", "Always Spawn")]
    [InlineData("_privateField", "Private Field")]
    [InlineData("ALLCAPS", "Allcaps")]
    [InlineData("already friendly name", "Already Friendly Name")]
    [InlineData("BP_DuctTape_C", "Duct Tape C")]
    [InlineData("DA_Thing", "Thing")]
    [InlineData("DT_Thing", "Thing")]
    [InlineData("Default__Thing", "Thing")]
    [InlineData("UIAmmoCount", "UI Ammo Count")]
    public void Unknown_field_is_auto_tidied(string technical, string expected)
    {
        Assert.Equal(expected, FieldNames.Friendly(technical));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_becomes_Value(string? technical)
    {
        Assert.Equal("Value", FieldNames.Friendly(technical));
    }

    [Fact]
    public void A_name_that_is_only_a_prefix_falls_back_to_Value()
    {
        Assert.Equal("Value", FieldNames.Friendly("BP_"));
        Assert.Equal("Value", FieldNames.Friendly("_"));
    }

    [Fact]
    public void Fallback_never_returns_empty_or_whitespace_for_non_empty_input()
    {
        var samples = new List<string>
        {
            "x", "X", "_", "BP_", "DT_", "Default__", "___", "123", "a1B2",
            "ALLCAPS", "bBool", "NPCSpawnerId", "SomeUnknownThing", "with spaces",
            "a", "Z9", "-", "..", "GridInventoryRowSpanExtra",
        };

        foreach (var sample in samples)
        {
            var label = FieldNames.Friendly(sample);
            Assert.False(string.IsNullOrWhiteSpace(label), $"'{sample}' produced a blank label");
        }
    }
}
