using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The food choices a cooking dish's ingredient rows pick from, and the one place the app's item id
/// is turned into the game's soft object path. Pure: assets in, choices out — no UI, no files.
/// </summary>
public class CookingFoodsTests
{
    private static AssetSummary Asset(string id, string name) =>
        new(id, name, "Food", StatusKind.Unknown, 0, id + ".uasset", null);

    // --- The app item id -> the game's soft path ------------------------------------------------

    [Fact]
    public void An_item_id_becomes_the_games_soft_path()
        => Assert.Equal(
            "/Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C",
            CookingFoods.SoftPath("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES"));

    [Fact]
    public void A_nested_item_id_keeps_its_folder()
        => Assert.Equal(
            "/Game/ConZ_Files/Items/Food/Meat/Cooked/Steak.Steak_C",
            CookingFoods.SoftPath("SCUM/Content/ConZ_Files/Items/Food/Meat/Cooked/Steak_ES"));

    [Theory]
    [InlineData("")]
    [InlineData("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple")]      // no _ES: not an editable item
    [InlineData("ConZ_Files/Items/Food/Fruit/Apple_ES")]               // not under SCUM/Content
    public void An_id_that_is_not_an_editable_item_has_no_soft_path(string id)
        => Assert.Equal(string.Empty, CookingFoods.SoftPath(id));

    // --- The friendly label ---------------------------------------------------------------------

    [Fact]
    public void A_soft_path_shows_a_plain_label()
        => Assert.Equal("Apple", CookingFoods.Label("/Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C"));

    [Fact]
    public void A_multi_word_soft_path_label_reads_as_words()
        => Assert.Equal("Cooked Steak",
            CookingFoods.Label("/Game/ConZ_Files/Items/Food/Meat/Cooked/Cooked_Steak.Cooked_Steak_C"));

    // --- The picker's choices -------------------------------------------------------------------

    [Fact]
    public void Only_food_items_become_choices()
    {
        var foods = CookingFoods.From(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES", "Apple"),
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Banana_ES", "Banana"),
            // Not a food: a tool, and a food's class file (no _ES).
            Asset("SCUM/Content/ConZ_Files/Items/Equipment/Axe_ES", "Axe"),
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple", "Apple class"),
        });

        Assert.Equal(2, foods.Count);
        Assert.All(foods, f => Assert.StartsWith("/Game/ConZ_Files/Items/Food/", f.SoftPath));
    }

    [Fact]
    public void The_choices_are_sorted_by_their_plain_label()
    {
        var foods = CookingFoods.From(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Pear_ES", "Pear"),
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES", "Apple"),
        });

        Assert.Equal(new[] { "Apple", "Pear" }, foods.Select(f => f.Label).ToArray());
    }

    [Fact]
    public void A_null_list_of_assets_yields_no_choices()
        => Assert.Empty(CookingFoods.From(null));

    [Fact]
    public void A_choice_carries_the_soft_path_and_the_label()
    {
        var food = Assert.Single(CookingFoods.From(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES", "Apple"),
        }));

        Assert.Equal("/Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C", food.SoftPath);
        Assert.Equal("Apple", food.Label);
    }
}
