using SCMW.Core.Content;
using SCMW.Core.Editing;

namespace SCMW.Core.Tests;

/// <summary>
/// How a dish's recorded list changes (add / remove an ingredient, add / remove a food) show on the
/// page: the file's own ingredients with the changes played back in order, so the card is right again
/// as soon as something is added or removed — and stays right when the dish is reopened.
/// </summary>
public class CookingIngredientListChangesTests
{
    private static AssetField F(string path, string value, string hint = "IntProperty") =>
        new(path, path, StatusKind.ReadOnly, value, hint, Nested: true);

    /// <summary>One main ingredient (Base) with one food (Apple), and one optional (Topping).</summary>
    private static IReadOnlyList<CookingIngredientSlot> TwoIngredients() => CookingIngredients.From(new[]
    {
        F("MainIngredients[0].IngredientTitle", "Base"),
        F("MainIngredients[0].PossibleIngredient[0].Item", "/Game/…/Apple.Apple_C", "SoftObjectProperty"),
        F("OptionalIngredients[0].IngredientTitle", "Topping"),
    });

    private static CookingListChange IngredientChange(ChangeOperation op, bool isMain, int index) =>
        new(op, isMain, index, IsFood: false, FoodIndex: -1);

    private static CookingListChange FoodChange(ChangeOperation op, bool isMain, int ingredient, int food) =>
        new(op, isMain, ingredient, IsFood: true, FoodIndex: food);

    [Fact]
    public void No_changes_leaves_the_ingredients_as_they_are()
    {
        var ingredients = CookingIngredients.WithListChanges(TwoIngredients(), Array.Empty<CookingListChange>());

        Assert.Equal(2, ingredients.Count);
        Assert.Equal("Base", ingredients[0].TitleValue);
        Assert.Equal("Topping", ingredients[1].TitleValue);
    }

    [Fact]
    public void A_null_change_list_leaves_the_ingredients_as_they_are()
        => Assert.Equal(2, CookingIngredients.WithListChanges(TwoIngredients(), null).Count);

    [Fact]
    public void An_add_appends_an_ingredient_cloned_from_the_last_of_its_own_list()
    {
        var ingredients = CookingIngredients.WithListChanges(TwoIngredients(),
            new[] { IngredientChange(ChangeOperation.AddEntry, isMain: true, index: -1) });

        Assert.Equal(3, ingredients.Count);
        Assert.True(ingredients[1].IsMain);                      // the main list's last, cloned
        Assert.Equal("Base", ingredients[1].TitleValue);
        Assert.Single(ingredients[1].Foods);
        Assert.Equal("Topping", ingredients[2].TitleValue);      // optional stays after main
    }

    [Fact]
    public void An_add_to_an_empty_list_makes_one_empty_ingredient()
    {
        var ingredients = CookingIngredients.WithListChanges(Array.Empty<CookingIngredientSlot>(),
            new[] { IngredientChange(ChangeOperation.AddEntry, isMain: true, index: -1) });

        var only = Assert.Single(ingredients);
        Assert.True(only.IsMain);
        Assert.Equal(0, only.Index);
        Assert.Null(only.Title);
        Assert.Empty(only.Foods);
    }

    [Fact]
    public void A_remove_drops_the_named_ingredient_and_renumbers_the_rest()
    {
        var ingredients = CookingIngredients.WithListChanges(TwoIngredients(),
            new[] { IngredientChange(ChangeOperation.RemoveEntry, isMain: true, index: 0) });

        var only = Assert.Single(ingredients);
        Assert.Equal(0, only.Index);
        Assert.Equal("Topping", only.TitleValue);
    }

    [Fact]
    public void A_remove_out_of_range_changes_nothing()
        => Assert.Equal(2, CookingIngredients.WithListChanges(TwoIngredients(),
            new[] { IngredientChange(ChangeOperation.RemoveEntry, isMain: true, index: 5) }).Count);

    [Fact]
    public void An_add_appends_a_food_cloned_from_the_ingredients_last_food()
    {
        var ingredients = CookingIngredients.WithListChanges(TwoIngredients(),
            new[] { FoodChange(ChangeOperation.AddEntry, isMain: true, ingredient: 0, food: -1) });

        var ingredient = ingredients[0];
        Assert.Equal(2, ingredient.Foods.Count);
        Assert.Equal("/Game/…/Apple.Apple_C", ingredient.Foods[1].ItemValue);   // cloned from food 0
    }

    [Fact]
    public void An_add_of_a_food_to_an_ingredient_with_none_makes_one_empty_food()
    {
        var ingredients = CookingIngredients.WithListChanges(TwoIngredients(),
            new[] { FoodChange(ChangeOperation.AddEntry, isMain: false, ingredient: 0, food: -1) });

        var food = Assert.Single(ingredients[1].Foods);
        Assert.Equal(0, food.Index);
        Assert.Null(food.Item);
    }

    [Fact]
    public void A_remove_drops_the_named_food_and_renumbers_the_rest()
    {
        var twoFoods = CookingIngredients.From(new[]
        {
            F("MainIngredients[0].PossibleIngredient[0].Item", "/Game/…/Apple.Apple_C", "SoftObjectProperty"),
            F("MainIngredients[0].PossibleIngredient[1].Item", "/Game/…/Pear.Pear_C", "SoftObjectProperty"),
        });

        var ingredients = CookingIngredients.WithListChanges(twoFoods,
            new[] { FoodChange(ChangeOperation.RemoveEntry, isMain: true, ingredient: 0, food: 0) });

        var food = Assert.Single(ingredients[0].Foods);
        Assert.Equal(0, food.Index);
        Assert.Equal("/Game/…/Pear.Pear_C", food.ItemValue);
    }

    [Fact]
    public void A_food_change_on_a_missing_ingredient_is_ignored()
        => Assert.Equal(2, CookingIngredients.WithListChanges(TwoIngredients(),
            new[] { FoodChange(ChangeOperation.RemoveEntry, isMain: true, ingredient: 9, food: 0) }).Count);
}
