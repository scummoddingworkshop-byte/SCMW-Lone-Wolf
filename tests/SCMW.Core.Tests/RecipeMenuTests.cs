using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The crafting menu's shelves and order. A recipe's shelf is its <c>CraftingCategoryTag.TagName</c>
/// (e.g. <c>CraftingCategory.Items.Clothing.ClothingRepairs</c>); the order inside a sub-group is its
/// <c>DisplayPriority</c>, lower first. The list itself is read from the game by the GameModel tool
/// and shipped with the app.
/// </summary>
public class RecipeMenuTests
{
    private static RecipePlace Place(string tag, string recipe, int priority) => new(tag, recipe, priority);

    [Fact]
    public void A_tag_splits_into_the_menu_shelf_and_its_block()
    {
        var group = Assert.Single(RecipeMenu.Groups(new[]
        {
            Place("CraftingCategory.Items.Clothing.ClothingRepairs", "CR_Improvised_Sewing_Kit", 1),
        }));

        Assert.Equal("Items", group.Tab);
        Assert.Equal("Clothing", group.Group);
        Assert.Equal("Clothing Repairs", group.SubGroup);
        Assert.Equal("Clothing → Clothing Repairs", group.Label);
    }

    [Fact]
    public void A_shelf_without_a_block_has_no_sub_group()
    {
        var group = Assert.Single(RecipeMenu.Groups(new[]
        {
            Place("CraftingCategory.Items.MeleeWeapons", "CR_1H_Stone_Axe", 4),
        }));

        Assert.Equal("Melee Weapons", group.Label);
        Assert.Null(group.SubGroup);
    }

    [Fact]
    public void Runs_of_capitals_keep_their_words_apart()
    {
        var group = Assert.Single(RecipeMenu.Groups(new[]
        {
            Place("CraftingCategory.BaseBuilding.Cosmetics.BBDecorPack", "CR_BB_Chair", 1),
        }));

        Assert.Equal("BB Decor Pack", group.SubGroup);
    }

    [Fact]
    public void A_tag_that_is_not_a_menu_shelf_is_ignored()
    {
        var groups = RecipeMenu.Groups(new[]
        {
            Place("CI_Olives", "CR_Olive", 1),
            Place("CraftingCategory.Items", "CR_Thing", 1),
        });

        Assert.Empty(groups);
    }

    [Fact]
    public void One_shelf_lists_its_recipes_in_menu_order()
    {
        var group = Assert.Single(RecipeMenu.Groups(new[]
        {
            Place("CraftingCategory.Items.Clothing.ClothingRepairs", "CR_Improvised_Sewing_Kit", 1),
            Place("CraftingCategory.Items.Clothing.ClothingRepairs", "CR_Bone_Needle", 0),
        }));

        Assert.Equal(new[] { "CR_Bone_Needle", "CR_Improvised_Sewing_Kit" },
            group.Recipes.Select(r => r.Recipe));
    }

    [Fact]
    public void Shelves_come_out_in_a_stable_order()
    {
        var groups = RecipeMenu.Groups(new[]
        {
            Place("CraftingCategory.Items.MeleeWeapons", "CR_Axe", 1),
            Place("CraftingCategory.Items.Clothing.ClothingRepairs", "CR_Kit", 1),
            Place("CraftingCategory.Items.Clothing.ClothesAndAccessories", "CR_Hat", 1),
        });

        Assert.Equal(new[]
        {
            "Clothing → Clothes And Accessories",
            "Clothing → Clothing Repairs",
            "Melee Weapons",
        }, groups.Select(g => g.Label));
    }

    [Fact]
    public void A_recipe_name_becomes_plain_words()
    {
        Assert.Equal("Improvised Sewing Kit", RecipeMenu.RecipeLabel("CR_Improvised_Sewing_Kit"));
        Assert.Equal("1H Stone Axe", RecipeMenu.RecipeLabel("CR_1H_Stone_Axe"));
        Assert.Equal("Hat", RecipeMenu.RecipeLabel("Hat"));
    }

    [Fact]
    public void Placing_at_the_end_takes_the_next_number()
    {
        Assert.Equal(0, RecipeMenu.AtEnd(Array.Empty<RecipePlace>()));
        Assert.Equal(2, RecipeMenu.AtEnd(new[] { Place("t", "CR_A", 0), Place("t", "CR_B", 1) }));
    }

    [Fact]
    public void Placing_at_the_end_refuses_when_the_top_number_is_taken()
    {
        Assert.Null(RecipeMenu.AtEnd(new[] { Place("t", "CR_A", 255) }));
    }

    [Fact]
    public void Placing_after_a_recipe_skips_a_number_already_taken()
    {
        var recipes = new[] { Place("t", "CR_A", 0), Place("t", "CR_B", 1) };
        Assert.Equal(2, RecipeMenu.After(recipes, 1));
    }

    [Fact]
    public void Placing_after_a_recipe_uses_the_gap_between_two_numbers()
    {
        var recipes = new[] { Place("t", "CR_A", 1), Place("t", "CR_B", 10) };
        Assert.Equal(2, RecipeMenu.After(recipes, 1));
    }

    [Fact]
    public void Placing_after_the_last_recipe_takes_the_next_free_number()
    {
        var recipes = new[] { Place("t", "CR_A", 1), Place("t", "CR_B", 2) };
        Assert.Equal(3, RecipeMenu.After(recipes, 2));
    }

    [Fact]
    public void Placing_after_a_number_near_the_top_refuses_when_no_number_is_left()
    {
        Assert.Null(RecipeMenu.After(new[] { Place("t", "CR_A", 255) }, 254));
        Assert.Null(RecipeMenu.After(Array.Empty<RecipePlace>(), 255));
    }
}
