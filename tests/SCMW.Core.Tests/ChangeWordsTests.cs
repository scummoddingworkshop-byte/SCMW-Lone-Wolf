using SCMW.Core.Content;
using Xunit;

namespace SCMW.Core.Tests;

/// <summary>
/// The words the Build list uses for a value's technical path. A path into a dish or a recipe is
/// written with the same numbering the cards use (so "optional ingredient 2" means the second one the
/// owner sees), and anything else falls back to the field library's own name.
/// </summary>
public class ChangeWordsTests
{
    [Theory]
    [InlineData("MainIngredients[0].IngredientTitle", "Main ingredient 1 · title")]
    [InlineData("OptionalIngredients[1].IsResource", "Optional ingredient 2 · is a resource")]
    [InlineData("OptionalIngredients[1].PossibleIngredient", "Optional ingredient 2 · foods")]
    [InlineData("OptionalIngredients[1].PossibleIngredient[0]", "Optional ingredient 2 · food 1")]
    [InlineData("OptionalIngredients[1].PossibleIngredient[0].Item", "Optional ingredient 2 · food 1 · item")]
    [InlineData("OptionalIngredients[1].PossibleIngredient[0].IsResource", "Optional ingredient 2 · food 1 · is a resource")]
    [InlineData("OptionalIngredients[1].PossibleIngredient[0].Liters", "Optional ingredient 2 · food 1 · liters")]
    [InlineData("OptionalIngredients[2]", "Optional ingredient 3")]
    public void A_dish_path_reads_like_the_cooking_card(string path, string words) =>
        Assert.Equal(words, ChangeWords.For(path));

    [Theory]
    [InlineData("Ingredients[2]", "Ingredient 3")]
    [InlineData("Ingredients[1].AllowedTypes[0]", "Ingredient 2 · what it accepts")]
    [InlineData("Ingredients[1].Amount.Basic", "Ingredient 2 · amount (basic)")]
    [InlineData("Ingredients[1].Amount.AboveAdvanced", "Ingredient 2 · amount (above advanced)")]
    public void A_recipe_path_reads_like_the_ingredients_card(string path, string words) =>
        Assert.Equal(words, ChangeWords.For(path));

    [Theory]
    [InlineData("MaxStackSize")]
    [InlineData("Weight")]
    [InlineData("CookingTime")]
    public void Anything_else_keeps_the_field_library_name(string path) =>
        Assert.Equal(FieldNames.Friendly(path), ChangeWords.For(path));
}
