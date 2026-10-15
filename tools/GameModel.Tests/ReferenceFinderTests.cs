namespace SCMW.GameModel;

/// <summary>Finds which family a field's value points at.</summary>
public class ReferenceFinderTests
{
    [Theory]
    [InlineData("Class'/Game/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Turret.BP_Base_Turret_C'", "base_elements")]
    [InlineData("ChoppingRecipeDataAsset'SCUM/Content/ConZ_Files/Items/Crafting/Chopping/Chop_Items/Chop_Destroy_Wooden_Chest.Chop_Destroy_Wooden_Chest'", "chopping")]
    [InlineData("PlaceableCraftingRecipe'SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Bed_Asian_Upgrade.CR_Bed_Asian_Upgrade'", "recipes")]
    [InlineData("CI_Metal_Scrap", "crafting_tags")]
    [InlineData("/Game/ConZ_Files/Vehicles/Car/WolfsWagen/WolfsWagen_ES.WolfsWagen_ES_C", "items")]
    [InlineData("/Game/UI/ST_UI_Crafting.ST_UI_Crafting", "string_tables")]
    public void Points_at_the_expected_family(string value, string expected)
        => Assert.Equal(expected, ReferenceFinder.Target(value));

    [Theory]
    [InlineData("StaticMesh'SCUM/Content/ConZ_Files/Models/Objects/SM_Asian_Chest_01.SM_Asian_Chest_01'")]
    [InlineData("Class'/Script/SCUM.SurvivalSkill'")]
    [InlineData("AkAudioEvent'SCUM/Content/WwiseAudio/Event/DefaultWorkUnit/Metal_Basebuilding_Element_Destroyed.Metal_Basebuilding_Element_Destroyed'")]
    [InlineData("5")]
    [InlineData("Key Card")]
    [InlineData("ST_Deena")]
    [InlineData("")]
    public void Points_at_nothing_for_a_value_that_is_not_a_family_asset(string value)
        => Assert.Null(ReferenceFinder.Target(value));
}
