using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The shipped menu list: every recipe's shelf and order, read from the installed game by the
/// GameModel tool (<c>--recipe-groups</c>) and embedded in the app. These tests pin the file's shape
/// and one well-known fact, so a broken or half-written list is caught before it reaches the screen.
/// </summary>
public class RecipeMenuDataTests
{
    [Fact]
    public void The_shipped_list_reads()
    {
        var data = RecipeMenuData.Load();

        Assert.False(string.IsNullOrWhiteSpace(data.GameBuild));
        Assert.False(string.IsNullOrWhiteSpace(data.ReadOn));
        Assert.True(data.Recipes.Count > 400);
    }

    [Fact]
    public void The_known_sewing_kit_shelf_is_in_the_list()
    {
        var data = RecipeMenuData.Load();

        var kit = Assert.Single(data.Recipes, r => r.Recipe == "CR_Improvised_Sewing_Kit");
        Assert.Equal("CraftingCategory.Items.Clothing.ClothingRepairs", kit.Tag);
        Assert.Equal(1, kit.Priority);
    }

    [Fact]
    public void Every_shipped_tag_is_a_real_menu_shelf()
    {
        var data = RecipeMenuData.Load();

        Assert.All(data.Recipes, recipe => Assert.NotNull(RecipeMenu.GroupOf(recipe.Tag)));
    }
}
