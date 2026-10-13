using SCMW.Core.Content;

namespace SCMW.Core.Tests;

public class AssetNamesTests
{
    [Fact]
    public void A_base_element_name_is_marked_so_it_cannot_collide_with_the_item()
    {
        // A placeable exists as both an item and a base element; the "(base)" marker keeps the
        // build's name-based lookup from seeing two identical names.
        Assert.Equal("Improvised Wooden Chest (base)",
            AssetNames.BaseElementFriendly("BP_Base_Improvised_Wooden_Chest"));
        Assert.Equal("Well (base)", AssetNames.BaseElementFriendly("BP_Base_Well"));
    }

    [Fact]
    public void A_base_element_is_recognised_and_an_item_is_not()
    {
        Assert.True(AssetNames.IsBaseElementName("BP_Base_Well"));
        Assert.False(AssetNames.IsBaseElementName("Improvised_Wooden_Chest"));
        Assert.False(AssetNames.IsBaseElementName(""));
    }

    [Fact]
    public void A_recipe_is_recognised_and_named_plainly()
    {
        Assert.True(AssetNames.IsRecipeName("CR_Base_Garden"));
        Assert.False(AssetNames.IsRecipeName("Improvised_Bow"));

        Assert.Equal("Base Garden", AssetNames.RecipeFriendly("CR_Base_Garden"));
        Assert.Equal("Wooden Arrow Feathers", AssetNames.RecipeFriendly("CR_Wooden_Arrow_Feathers"));
    }

    [Fact]
    public void A_recipe_is_grouped_by_the_folder_under_Recipes()
    {
        Assert.Equal("Placeables",
            AssetNames.RecipeCategory("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden.uasset"));
        Assert.Equal("Items",
            AssetNames.RecipeCategory("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Wooden_Arrow.uasset"));
        Assert.Equal("Recipes", AssetNames.RecipeCategory("SCUM/Content/ConZ_Files/Items/Other/CR_Thing.uasset"));
    }
}
