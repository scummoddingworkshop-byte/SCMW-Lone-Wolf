namespace SCMW.GameModel;

/// <summary>
/// The seven gameplay families the first sweep covers, and the rule that puts a
/// pak file into one. Rules are naming/folder habits copied from the earlier
/// sampled extract (docs/research/game-structure/families.json) and the app's own
/// item/base-element/spawner roots. Paths use the game's own '/' separators.
/// </summary>
public class FamilyRulesTests
{
    [Theory]
    [InlineData("SCUM/Content/ConZ_Files/Vehicles/Car/WolfsWagen/WolfsWagen_ES.uasset", "items")]
    [InlineData("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Group_BluntTool.uasset", "crafting_tags")]
    [InlineData("SCUM/Content/ConZ_Files/Items/Crafting/Chopping/Chop_Drone.uasset", "chopping")]
    [InlineData("SCUM/Content/ConZ_Files/UI/StringTables/ST_UI_Crafting.uasset", "string_tables")]
    [InlineData("SCUM/Plugins/GameFeatures/ApexHunterPack/Content/Gameplay/BaseBuilding/Data/CR_Base_Trophy_Bear_Skull.uasset", "recipes")]
    [InlineData("SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Advanced_Standing_Torch.uasset", "base_elements")]
    [InlineData("SCUM/Content/ConZ_Files/Characters/SpawnerPresets/FishSpeciesPresets/SpeciesPreset_River_Big_Radioactive_Activity_Spot.uasset", "spawners")]
    [InlineData("SCUM/Content/ConZ_Files/Items/SpawnerPresets2/Something_Here.uasset", "spawners")]
    public void Puts_a_path_in_the_expected_family(string path, string expected)
        => Assert.Equal(expected, FamilyRules.FamilyOf(path));

    [Theory]
    [InlineData("SCUM/Content/ConZ_Files/Models/Objects/Outdoor/Barricades/Textures/T_Barbed_Wire_Barricade_01_N.uasset")]
    [InlineData("SCUM/Content/ConZ_Files/Models/Objects/Items/Fortifications/Wooden/SM_Wooden_Fortifications_Window_Triple_01.uasset")]
    public void Returns_null_for_an_asset_that_is_not_one_of_the_families(string path)
        => Assert.Null(FamilyRules.FamilyOf(path));

    [Fact]
    public void Ignores_a_file_that_is_not_a_uasset()
        => Assert.Null(FamilyRules.FamilyOf("SCUM/Content/ConZ_Files/Vehicles/Car/WolfsWagen/WolfsWagen_ES.uexp"));

    [Fact]
    public void Matches_case_insensitively()
        => Assert.Equal("items", FamilyRules.FamilyOf("scum/content/conz_files/vehicles/wolfswagen/wolfswagen_es.uasset"));

    [Fact]
    public void Lists_the_seven_families_in_a_stable_order()
        => Assert.Equal(
            new[] { "items", "recipes", "base_elements", "crafting_tags", "chopping", "spawners", "string_tables" },
            FamilyRules.Families.ToArray());
}
