using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The one place an item becomes the game's soft object path — the "/Game/…" link a recipe's
/// "Product" and a dish's food both store. Pure: an item id or package path in, a soft path out.
/// </summary>
public class ItemSoftPathsTests
{
    // --- An editable item file id -> the game's soft path ---------------------------------------

    [Fact]
    public void An_item_file_id_becomes_the_games_soft_path()
        => Assert.Equal(
            "/Game/ConZ_Files/Items/Crafting_Items/Olives.Olives_C",
            ItemSoftPaths.FromItemFile("SCUM/Content/ConZ_Files/Items/Crafting_Items/Olives_ES"));

    [Fact]
    public void A_nested_item_file_id_keeps_its_folder()
        => Assert.Equal(
            "/Game/ConZ_Files/Items/Food/Meat/Cooked/Steak.Steak_C",
            ItemSoftPaths.FromItemFile("SCUM/Content/ConZ_Files/Items/Food/Meat/Cooked/Steak_ES"));

    [Theory]
    [InlineData("")]
    [InlineData("SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple")]   // no _ES: not an editable item
    [InlineData("ConZ_Files/Items/Food/Fruit/Apple_ES")]            // not under SCUM/Content
    public void A_file_id_that_is_not_an_editable_item_has_no_soft_path(string id)
        => Assert.Equal(string.Empty, ItemSoftPaths.FromItemFile(id));

    // --- A new item's own package path -> the game's soft path ---------------------------------

    [Fact]
    public void A_new_items_package_becomes_the_games_soft_path()
        => Assert.Equal(
            "/Game/ConZ_Files/Items/Crafting_Items/SCMW_OlivePress/OliveOil.OliveOil_C",
            ItemSoftPaths.FromItemPackage("SCUM/Content/ConZ_Files/Items/Crafting_Items/SCMW_OlivePress/OliveOil"));

    [Fact]
    public void A_package_path_outside_the_game_folder_has_no_soft_path()
        => Assert.Equal(string.Empty, ItemSoftPaths.FromItemPackage("Mod/OliveOil"));

    // --- A soft path back to the package it names -----------------------------------------------

    [Fact]
    public void A_soft_path_names_its_package()
        => Assert.Equal(
            "SCUM/Content/ConZ_Files/Items/Equipment/AnimalBaitFeederItem",
            ItemSoftPaths.PackageOf(
                "/Game/ConZ_Files/Items/Equipment/AnimalBaitFeederItem.AnimalBaitFeederItem_C"));

    [Theory]
    [InlineData("")]
    [InlineData("ConZ_Files/Items/Equipment/Thing.Thing_C")]   // not under /Game
    [InlineData("/Game")]                                      // no package
    public void A_value_that_is_not_a_game_soft_path_names_no_package(string softPath)
        => Assert.Equal(string.Empty, ItemSoftPaths.PackageOf(softPath));

    // --- The friendly label ---------------------------------------------------------------------

    [Fact]
    public void A_soft_path_shows_a_plain_label()
        => Assert.Equal("Olive Oil",
            ItemSoftPaths.Label("/Game/ConZ_Files/Items/Crafting_Items/SCMW_OlivePress/Olive_Oil.Olive_Oil_C"));
}
