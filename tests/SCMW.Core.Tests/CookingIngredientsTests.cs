using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The pure per-slot model of a cooking dish's ingredients: take the nested
/// <c>MainIngredients[N]…</c> / <c>OptionalIngredients[N]…</c> fields the reader returns and group
/// them into one ingredient per index, each carrying its title and the foods it may take. No UI, no
/// files — plain data in, plain data out.
/// </summary>
public class CookingIngredientsTests
{
    /// <summary>One nested field as the reader emits it: friendly name = technical dotted path.</summary>
    private static AssetField F(string path, string value, string hint = "IntProperty") =>
        new(path, path, StatusKind.ReadOnly, value, hint, Nested: true);

    [Fact]
    public void No_fields_yields_no_ingredients()
        => Assert.Empty(CookingIngredients.From(Array.Empty<AssetField>()));

    [Fact]
    public void A_null_field_list_yields_no_ingredients()
        => Assert.Empty(CookingIngredients.From(null));

    [Fact] // CookingTime and the exhaustion bonuses are the dish's own numbers, not an ingredient
    public void A_field_that_is_not_an_ingredient_is_ignored()
    {
        var fields = new[]
        {
            F("CookingTime", "30"),
            F("ExhaustionBonusPerMainIngredient", "5"),
        };

        Assert.Empty(CookingIngredients.From(fields));
    }

    [Fact]
    public void A_main_ingredient_is_read_with_its_title()
    {
        var ingredient = Assert.Single(CookingIngredients.From(new[]
        {
            F("MainIngredients[0].IngredientTitle", "Base"),
        }));

        Assert.True(ingredient.IsMain);
        Assert.Equal(0, ingredient.Index);
        Assert.Equal("Base", ingredient.TitleValue);
    }

    [Fact]
    public void Main_and_optional_ingredients_are_kept_apart()
    {
        var ingredients = CookingIngredients.From(new[]
        {
            F("MainIngredients[0].IngredientTitle", "Base"),
            F("OptionalIngredients[0].IngredientTitle", "Topping"),
        });

        Assert.Equal(new[] { "Base", "Topping" }, ingredients.Select(i => i.TitleValue).ToArray());
        Assert.True(ingredients[0].IsMain);
        Assert.False(ingredients[1].IsMain);
    }

    [Fact]
    public void A_food_is_read_with_its_item_and_values()
    {
        var ingredient = Assert.Single(CookingIngredients.From(new[]
        {
            F("MainIngredients[0].PossibleIngredient[0].Item", "/Game/…/Apple.Apple_C", "SoftObjectProperty"),
            F("MainIngredients[0].PossibleIngredient[0].Liters", "0.5", "FloatProperty"),
            F("MainIngredients[0].PossibleIngredient[0].Usage", "1"),
            F("MainIngredients[0].PossibleIngredient[0].NutrientInclusionFactor", "1.0", "FloatProperty"),
            F("MainIngredients[0].PossibleIngredient[0].ShouldConsumeWhole", "False", "BoolProperty"),
            F("MainIngredients[0].PossibleIngredient[0].IsResource", "False", "BoolProperty"),
        }));

        var food = Assert.Single(ingredient.Foods);
        Assert.Equal(0, food.Index);
        Assert.Equal("/Game/…/Apple.Apple_C", food.ItemValue);
        Assert.Equal("0.5", food.ValueField("Liters")!.CurrentValue);
        Assert.Equal("1", food.ValueField("Usage")!.CurrentValue);
        Assert.Equal("False", food.ValueField("ShouldConsumeWhole")!.CurrentValue);
    }

    [Fact] // ResourceType is an object link we deliberately leave to the plain field list
    public void A_food_field_we_do_not_own_is_not_a_value()
    {
        var ingredient = Assert.Single(CookingIngredients.From(new[]
        {
            F("MainIngredients[0].PossibleIngredient[0].ResourceType", "/Game/…/Some.Type", "ObjectProperty"),
        }));

        var food = Assert.Single(ingredient.Foods);
        Assert.Null(food.ValueField("ResourceType"));
        Assert.Empty(food.Values);
    }

    [Fact]
    public void Foods_come_back_in_their_own_order()
    {
        var ingredient = Assert.Single(CookingIngredients.From(new[]
        {
            F("MainIngredients[0].PossibleIngredient[1].Item", "/Game/…/Pear.Pear_C", "SoftObjectProperty"),
            F("MainIngredients[0].PossibleIngredient[0].Item", "/Game/…/Apple.Apple_C", "SoftObjectProperty"),
        }));

        Assert.Equal(new[] { 0, 1 }, ingredient.Foods.Select(f => f.Index).ToArray());
        Assert.Equal("/Game/…/Apple.Apple_C", ingredient.Foods[0].ItemValue);
    }

    [Fact]
    public void Ingredients_are_ordered_by_index_even_when_the_fields_are_shuffled()
    {
        var ingredients = CookingIngredients.From(new[]
        {
            F("MainIngredients[1].IngredientTitle", "Second"),
            F("MainIngredients[0].IngredientTitle", "First"),
        });

        Assert.Equal(new[] { 0, 1 }, ingredients.Select(i => i.Index).ToArray());
    }

    // --- The write paths (the deliberate exception to the field-library rule) ---------------------
    // A dish's ingredient and food values are per-slot paths the fixed library cannot express, so
    // these helpers are the ONE place that names them; the writer already resolves exactly these paths.

    [Fact]
    public void The_ingredient_paths_rebuild_the_files_own_names()
    {
        Assert.Equal("MainIngredients", CookingIngredients.ListName(isMain: true));
        Assert.Equal("OptionalIngredients", CookingIngredients.ListName(isMain: false));
        Assert.Equal("MainIngredients[2]", CookingIngredients.IngredientPath(true, 2));
        Assert.Equal("MainIngredients[2].IngredientTitle", CookingIngredients.TitlePath(true, 2));
        Assert.Equal("OptionalIngredients[1].PossibleIngredient", CookingIngredients.FoodListPath(false, 1));
        Assert.Equal("OptionalIngredients[1].PossibleIngredient[3]", CookingIngredients.FoodPath(false, 1, 3));
        Assert.Equal("MainIngredients[0].PossibleIngredient[1].Item", CookingIngredients.FoodItemPath(true, 0, 1));
        Assert.Equal("MainIngredients[0].PossibleIngredient[1].Liters",
            CookingIngredients.FoodValuePath(true, 0, 1, "Liters"));
    }

    [Fact]
    public void A_saved_list_path_can_be_read_back()
    {
        Assert.True(CookingIngredients.TryListPath("MainIngredients", out var add));
        Assert.True(add.IsMain);
        Assert.Equal(-1, add.IngredientIndex);
        Assert.False(add.IsFood);

        Assert.True(CookingIngredients.TryListPath("OptionalIngredients[4]", out var dropIngredient));
        Assert.False(dropIngredient.IsMain);
        Assert.Equal(4, dropIngredient.IngredientIndex);
        Assert.False(dropIngredient.IsFood);

        Assert.True(CookingIngredients.TryListPath("MainIngredients[2].PossibleIngredient", out var addFood));
        Assert.Equal(2, addFood.IngredientIndex);
        Assert.True(addFood.IsFood);
        Assert.Equal(-1, addFood.FoodIndex);

        Assert.True(CookingIngredients.TryListPath("MainIngredients[2].PossibleIngredient[3]", out var dropFood));
        Assert.True(dropFood.IsFood);
        Assert.Equal(3, dropFood.FoodIndex);
    }

    [Fact]
    public void A_path_that_is_not_a_list_change_is_not_read()
    {
        Assert.False(CookingIngredients.TryListPath("CookingTime", out _));
        Assert.False(CookingIngredients.TryListPath("MainIngredients[0].IngredientTitle", out _));
        Assert.False(CookingIngredients.TryListPath(null, out _));
    }
}
