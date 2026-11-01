using SCMW.Backend.Build;
using SCMW.Core.Editing;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The writer's list add/remove, tested without a game package (the same little name table the
/// other writer tests use). This is the code that only the env-gated real-install tests used to
/// touch, so it runs everywhere now.
/// </summary>
public class UAssetPackageWriterListTests
{
    private readonly FakeNameMap _names = new();

    private StructPropertyData Slot(int amount) =>
        new(_names.Name("Ingredients"))
        {
            Value = new List<PropertyData> { new IntPropertyData(_names.Name("NoSkill")) { Value = amount } },
        };

    private ArrayPropertyData Ingredients(params PropertyData[] slots) =>
        new(_names.Name("Ingredients")) { Value = slots };

    /// <summary>A slot that carries a nested list holding an object link (like a recipe ingredient).</summary>
    private StructPropertyData TaggedSlot(int amount, int importIndex) =>
        new(_names.Name("Ingredients"))
        {
            Value = new List<PropertyData>
            {
                new ArrayPropertyData(_names.Name("AllowedTypes"))
                {
                    Value = new PropertyData[]
                    {
                        new ObjectPropertyData(_names.Name("AllowedTypes"))
                        {
                            Value = FPackageIndex.FromImport(importIndex),
                        },
                    },
                },
                new IntPropertyData(_names.Name("NoSkill")) { Value = amount },
            },
        };

    private static int Amount(PropertyData slot) =>
        ((IntPropertyData)((StructPropertyData)slot).Value[0]).Value;

    [Fact]
    public void Adding_an_entry_clones_the_last_one()
    {
        var array = Ingredients(Slot(4), Slot(5));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.Null(error);
        Assert.Equal(3, array.Value.Length);
        Assert.Equal(5, Amount(array.Value[2]));
        Assert.NotSame(array.Value[1], array.Value[2]);   // a real copy, not an alias
    }

    [Fact]
    public void Adding_an_entry_deep_clones_the_nested_fields()
    {
        var array = Ingredients(TaggedSlot(4, 2), TaggedSlot(5, 3));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out _, out _));

        var source = (StructPropertyData)array.Value[1];
        var clone = (StructPropertyData)array.Value[2];

        var sourceLink = (ObjectPropertyData)((ArrayPropertyData)source.Value[0]).Value[0];
        var cloneLink = (ObjectPropertyData)((ArrayPropertyData)clone.Value[0]).Value[0];

        // The nested objects must be copies, or editing the new slot rewrites the slot it copied.
        Assert.NotSame(sourceLink, cloneLink);

        cloneLink.Value = FPackageIndex.FromImport(0);
        Assert.NotEqual(sourceLink.Value.ToString(), cloneLink.Value.ToString());
    }

    /// <summary>What one build remembers: the entry shape of each list it emptied (keyed by the list).</summary>
    private static Dictionary<object, PropertyData> Remembered() =>
        new(ReferenceEqualityComparer.Instance);

    [Fact]
    public void An_entry_can_be_added_back_after_the_project_emptied_the_list()
    {
        var array = Ingredients(Slot(4));
        var properties = new PropertyData[] { array };
        var remembered = Remembered();

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            properties, new FieldChange("Ingredients[0]", "", ChangeOperation.RemoveEntry),
            out _, out var removeError, remembered), removeError);
        Assert.Empty(array.Value);

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            properties, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out _, out var addError, remembered), addError);

        var added = Assert.Single(array.Value);
        Assert.Equal(4, Amount(added));
    }

    [Fact]
    public void The_entry_added_back_is_a_copy_of_the_one_that_was_removed()
    {
        var array = Ingredients(TaggedSlot(4, 2));
        var properties = new PropertyData[] { array };
        var remembered = Remembered();
        var removed = array.Value[0];

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            properties, new FieldChange("Ingredients[0]", "", ChangeOperation.RemoveEntry),
            out _, out _, remembered));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            properties, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out _, out _, remembered));

        var added = Assert.Single(array.Value);
        Assert.NotSame(removed, added);                       // a fresh entry, not the old object
        var addedLink = (ObjectPropertyData)((ArrayPropertyData)((StructPropertyData)added).Value[0]).Value[0];
        var removedLink = (ObjectPropertyData)((ArrayPropertyData)((StructPropertyData)removed).Value[0]).Value[0];
        Assert.NotSame(removedLink, addedLink);                // and its nested fields stand alone
    }

    [Fact]
    public void Adding_to_an_emptied_list_is_refused_when_nothing_was_remembered()
    {
        var array = Ingredients();

        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(array.Value);
    }

    [Fact]
    public void Removing_an_entry_drops_the_named_one()
    {
        var array = Ingredients(Slot(4), Slot(5), Slot(6));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients[1]", "", ChangeOperation.RemoveEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.Null(error);
        Assert.Equal(2, array.Value.Length);
        Assert.Equal(4, Amount(array.Value[0]));
        Assert.Equal(6, Amount(array.Value[1]));
    }

    [Fact]
    public void Removing_an_out_of_range_entry_is_refused()
    {
        var array = Ingredients(Slot(4));

        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients[9]", "", ChangeOperation.RemoveEntry),
            out var found, out var error));

        Assert.True(found);                                // the list was found...
        Assert.False(string.IsNullOrWhiteSpace(error));    // ...but the entry was not
        Assert.Single(array.Value);
    }

    [Fact]
    public void A_field_that_is_not_a_list_is_not_found()
    {
        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { new IntPropertyData(_names.Name("Weight")) { Value = 3 } },
            new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.False(found);
    }

    [Fact]
    public void Adding_beyond_the_cap_is_refused()
    {
        var slots = Enumerable.Range(0, 32).Select(i => (PropertyData)Slot(i)).ToArray();
        var array = Ingredients(slots);

        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(32, array.Value.Length);   // nothing was added
    }

    // --- A nested list (a dish ingredient's foods) ------------------------------------------------
    // The cooking Ingredients card adds and removes foods on a list one level down
    // (MainIngredients[0].PossibleIngredient); this proves the writer already resolves it.

    private StructPropertyData Food(int usage) =>
        new(_names.Name("PossibleIngredient"))
        {
            Value = new List<PropertyData>
            {
                new SoftObjectPropertyData(_names.Name("Item"))
                {
                    Value = new FSoftObjectPath(
                        _names.Name("/Game"),
                        _names.Name("/Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C"),
                        FString.FromString(string.Empty)),
                },
                new IntPropertyData(_names.Name("Usage")) { Value = usage },
            },
        };

    private StructPropertyData Ingredient(params PropertyData[] foods) =>
        new(_names.Name("MainIngredients"))
        {
            Value = new List<PropertyData>
            {
                new ArrayPropertyData(_names.Name("PossibleIngredient")) { Value = foods },
            },
        };

    private ArrayPropertyData MainIngredients(params PropertyData[] ingredients) =>
        new(_names.Name("MainIngredients")) { Value = ingredients };

    private static int Usage(PropertyData food) =>
        ((IntPropertyData)((StructPropertyData)food).Value[1]).Value;

    private static ArrayPropertyData Foods(ArrayPropertyData main) =>
        (ArrayPropertyData)((StructPropertyData)main.Value[0]).Value[0];

    [Fact]
    public void Adding_a_food_clones_the_ingredients_last_food()
    {
        var main = MainIngredients(Ingredient(Food(1)));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { main },
            new FieldChange("MainIngredients[0].PossibleIngredient", "", ChangeOperation.AddEntry),
            out var found, out var error), error);

        Assert.True(found);
        var foods = Foods(main);
        Assert.Equal(2, foods.Value.Length);
        Assert.Equal(1, Usage(foods.Value[1]));
        Assert.NotSame(foods.Value[0], foods.Value[1]);
        // The nested soft link is copied too, or editing the new food rewrites the one it copied.
        Assert.NotSame(((StructPropertyData)foods.Value[0]).Value[0],
                       ((StructPropertyData)foods.Value[1]).Value[0]);
    }

    [Fact]
    public void Removing_a_food_drops_the_named_one()
    {
        var main = MainIngredients(Ingredient(Food(1), Food(2)));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { main },
            new FieldChange("MainIngredients[0].PossibleIngredient[0]", "", ChangeOperation.RemoveEntry),
            out var found, out var error), error);

        Assert.True(found);
        var foods = Foods(main);
        Assert.Single(foods.Value);
        Assert.Equal(2, Usage(foods.Value[0]));
    }
}
