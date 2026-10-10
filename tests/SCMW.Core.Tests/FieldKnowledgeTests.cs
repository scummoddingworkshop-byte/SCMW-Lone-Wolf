// tests/SCMW.Core.Tests/FieldKnowledgeTests.cs
// Task 4: presentation-only field knowledge. Groups map SCUM's fields to friendly
// sections (explicit dictionary first, then keywords, else Other); IsInternalNoise is
// a small curated list; FormatValue renders raw values plainly and never throws.
using SCMW.Core.Content;
using Xunit;

namespace SCMW.Core.Tests;

public class FieldKnowledgeTests
{
    [Theory]
    [InlineData("GridInventoryRowSpan", FieldGroup.Inventory)]
    [InlineData("GridInventoryColumnSpan", FieldGroup.Inventory)]
    [InlineData("GridInventoryIcon", FieldGroup.Inventory)]
    [InlineData("MaxStackSize", FieldGroup.Inventory)]
    [InlineData("ItemDropSize", FieldGroup.Inventory)]
    [InlineData("Weight", FieldGroup.Inventory)]
    [InlineData("SlotType", FieldGroup.Inventory)]
    [InlineData("Damage", FieldGroup.Gameplay)]
    [InlineData("ItemDamage", FieldGroup.Gameplay)]
    [InlineData("HealAmount", FieldGroup.Gameplay)]
    [InlineData("Health", FieldGroup.Gameplay)]
    [InlineData("WaterCapacity", FieldGroup.Gameplay)]
    [InlineData("Calories", FieldGroup.Gameplay)]
    [InlineData("CraftingSkill", FieldGroup.Gameplay)]
    [InlineData("RequiredSkill", FieldGroup.Gameplay)]
    [InlineData("SpawnRate", FieldGroup.Gameplay)]
    [InlineData("AmmoType", FieldGroup.Gameplay)]
    [InlineData("DrawTime", FieldGroup.Gameplay)]
    [InlineData("AttackSpeed", FieldGroup.Gameplay)]
    [InlineData("Range", FieldGroup.Gameplay)]
    [InlineData("Durability", FieldGroup.Gameplay)]
    [InlineData("RepairValue", FieldGroup.Gameplay)]
    [InlineData("CamoPattern", FieldGroup.Appearance)]
    [InlineData("StaticMesh", FieldGroup.Appearance)]
    [InlineData("Material", FieldGroup.Appearance)]
    [InlineData("BodyTexture", FieldGroup.Appearance)]
    [InlineData("Icon", FieldGroup.Appearance)]
    [InlineData("VehicleColor", FieldGroup.Appearance)]
    [InlineData("Visual", FieldGroup.Appearance)]
    [InlineData("ItemName", FieldGroup.ItemSettings)]
    [InlineData("DisplayName", FieldGroup.ItemSettings)]
    [InlineData("ItemClass", FieldGroup.ItemSettings)]
    [InlineData("ItemType", FieldGroup.ItemSettings)]
    [InlineData("ItemRarity", FieldGroup.ItemSettings)]
    [InlineData("TagName", FieldGroup.ItemSettings)]
    [InlineData("DespawnTime", FieldGroup.ItemSettings)]
    [InlineData("SomeUnknownThing", FieldGroup.Other)]
    [InlineData("", FieldGroup.Other)]
    public void GroupFor_maps_a_field_to_its_section(string technical, FieldGroup expected)
    {
        Assert.Equal(expected, FieldKnowledge.GroupFor(technical));
    }

    [Theory]
    [InlineData("gridinventoryrowspan", FieldGroup.Inventory)] // case-insensitive
    [InlineData("DAMAGE", FieldGroup.Gameplay)]
    [InlineData("camoPATTERN", FieldGroup.Appearance)]
    [InlineData("itemrarity", FieldGroup.ItemSettings)]
    public void GroupFor_ignores_case(string technical, FieldGroup expected)
    {
        Assert.Equal(expected, FieldKnowledge.GroupFor(technical));
    }

    [Theory]
    [InlineData("ImportData", true)]
    [InlineData("AssetImportData", true)]
    [InlineData("ThumbnailInfo", true)]
    [InlineData("bReplicates", true)]
    [InlineData("bActorTickEnabled", true)]
    [InlineData("bIsEditorOnlyActor", true)]
    [InlineData("thumbnailinfo", true)] // case-insensitive
    [InlineData("Weight", false)]
    [InlineData("Damage", false)]
    [InlineData("GridInventoryRowSpan", false)]
    [InlineData("ItemName", false)]
    [InlineData("", false)]
    public void IsInternalNoise_flags_only_curated_engine_bookkeeping(string technical, bool expected)
    {
        Assert.Equal(expected, FieldKnowledge.IsInternalNoise(technical));
    }

    [Fact]
    public void IsInternalNoise_isfalse_for_null()
    {
        Assert.False(FieldKnowledge.IsInternalNoise(null));
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData("", "-")]
    [InlineData("   ", "-")]
    [InlineData("None", "-")]
    [InlineData("none", "-")]
    [InlineData("true", "Yes")]
    [InlineData("True", "Yes")]
    [InlineData("false", "No")]
    [InlineData("False", "No")]
    [InlineData("42", "42")]
    [InlineData("1000000", "1000000")]
    [InlineData("1.50", "1.5")]
    [InlineData("0.30", "0.3")]
    [InlineData("/Game/Items/Weapons/BP_Bow.BP_Bow", "BP_Bow")]
    [InlineData("SCUM/Content/Items/Equipment/BP_DuctTape_C", "BP_DuctTape_C")]
    [InlineData("[a, b, c]", "a, b, c")]
    [InlineData("a,b,c", "a, b, c")]
    [InlineData("Woodland", "Woodland")]
    public void FormatValue_renders_raw_values_plainly(string? raw, string expected)
    {
        Assert.Equal(expected, FieldKnowledge.FormatValue(raw));
    }

    [Fact]
    public void FormatValue_never_throws_for_an_object_whose_tostring_throws()
    {
        Assert.Equal("-", FieldKnowledge.FormatValue(new ThrowingValue()));
    }

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("boom");
    }
}
