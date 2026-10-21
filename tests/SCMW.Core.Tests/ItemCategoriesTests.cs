using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;

namespace SCMW.Core.Tests;

/// <summary>
/// The pure model of "what an item counts as": read the nested entries the reader returns, play back
/// add/remove records, and pick the game's own categories. Plain data in, plain data out.
/// </summary>
public class ItemCategoriesTests
{
    private static AssetField F(string path, string value) =>
        new(path, path, StatusKind.ReadOnly, value, "ObjectProperty", Nested: true);

    private static AssetSummary Asset(string id) =>
        new(id, id[(id.LastIndexOf('/') + 1)..], "Crafting", StatusKind.ReadOnly, 0, id, null);

    [Fact]
    public void No_fields_yields_no_entries()
        => Assert.Empty(ItemCategories.From(Array.Empty<AssetField>()));

    [Fact]
    public void The_category_and_amount_are_read_by_entry()
    {
        var entries = ItemCategories.From(new[]
        {
            F("CraftingIngredientInstances[0].Type", "CI_Group_Canned_Vegetables"),
            F("CraftingIngredientInstances[0].Amount", "1"),
        });

        var entry = Assert.Single(entries);
        Assert.Equal(0, entry.Index);
        Assert.Equal("CI_Group_Canned_Vegetables", entry.TagName);
        Assert.Equal("1", entry.AmountValue);
    }

    [Fact]
    public void An_entry_that_points_at_a_whole_path_shows_only_the_name()
    {
        var entry = Assert.Single(ItemCategories.From(new[]
        {
            F("CraftingIngredientInstances[0].Type",
              "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives"),
        }));

        Assert.Equal("CI_Olives", entry.TagName);
    }

    [Fact]
    public void Fields_that_are_not_entries_are_ignored()
    {
        Assert.Empty(ItemCategories.From(new[]
        {
            F("CraftingIngredientInstances", "StructProperty[1]"),
            F("Weight", "0.2"),
        }));
    }

    [Fact]
    public void Adding_an_entry_clones_the_last_one()
    {
        var entries = ItemCategories.From(new[]
        {
            F("CraftingIngredientInstances[0].Type", "CI_Cans"),
        });

        var displayed = ItemCategories.WithListChanges(entries,
            new[] { new ItemCategoryListChange(ChangeOperation.AddEntry, -1) });

        Assert.Equal(2, displayed.Count);
        Assert.Equal("CI_Cans", displayed[1].TagName);
        Assert.Equal(1, displayed[1].Index);
    }

    [Fact]
    public void Removing_an_entry_drops_it_and_renumbers()
    {
        var entries = ItemCategories.From(new[]
        {
            F("CraftingIngredientInstances[0].Type", "CI_Cans"),
            F("CraftingIngredientInstances[1].Type", "CI_Olives"),
        });

        var displayed = ItemCategories.WithListChanges(entries,
            new[] { new ItemCategoryListChange(ChangeOperation.RemoveEntry, 0) });

        var entry = Assert.Single(displayed);
        Assert.Equal("CI_Olives", entry.TagName);
        Assert.Equal(0, entry.Index);
    }

    [Fact]
    public void The_games_categories_are_listed_once_and_sorted_by_label()
    {
        var categories = ItemCategories.CategoriesFrom(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Vegetables"),
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Meat"),
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Meat"),
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Olives_ES"),
        });

        Assert.Equal(new[] { "CI_Meat", "CI_Vegetables" }, categories.Select(c => c.Name));
        Assert.Equal("Meat", categories[0].Label);
        Assert.Equal("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Meat", categories[0].Path);
    }
}
