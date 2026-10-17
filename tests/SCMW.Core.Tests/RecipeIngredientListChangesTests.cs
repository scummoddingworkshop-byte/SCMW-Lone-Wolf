using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;

namespace SCMW.Core.Tests;

/// <summary>
/// How a recipe's recorded list changes (add / remove an ingredient slot) show on the page: the
/// file's own slots with the changes played back in order, so the card is right again as soon as a
/// slot is added or removed — and stays right when the recipe is reopened.
/// </summary>
public class RecipeIngredientListChangesTests
{
    private static AssetField F(string path, string value) =>
        new(path, path, StatusKind.ReadOnly, value, "IntProperty", Nested: true);

    /// <summary>Two real slots: 0 accepts Plank (13), 1 accepts Metal Scrap (4).</summary>
    private static IReadOnlyList<IngredientSlot> TwoSlots() => RecipeIngredients.From(new[]
    {
        F("Ingredients[0].AllowedTypes[0]", "CI_Plank"),
        F("Ingredients[0].Amount.NoSkill", "13"),
        F("Ingredients[1].AllowedTypes[0]", "CI_Metal_Scrap"),
        F("Ingredients[1].Amount.NoSkill", "4"),
    });

    [Fact]
    public void No_changes_leaves_the_slots_as_they_are()
    {
        var slots = RecipeIngredients.WithListChanges(TwoSlots(), Array.Empty<IngredientListChange>());

        Assert.Equal(new[] { 0, 1 }, slots.Select(s => s.Index).ToArray());
        Assert.Equal("CI_Plank", slots[0].TagValue);
    }

    [Fact]
    public void A_null_change_list_leaves_the_slots_as_they_are()
        => Assert.Equal(2, RecipeIngredients.WithListChanges(TwoSlots(), null).Count);

    [Fact]
    public void An_add_appends_a_slot_cloned_from_the_last()
    {
        var slots = RecipeIngredients.WithListChanges(TwoSlots(),
            new[] { new IngredientListChange(ChangeOperation.AddEntry, -1) });

        Assert.Equal(new[] { 0, 1, 2 }, slots.Select(s => s.Index).ToArray());
        Assert.Equal("CI_Metal_Scrap", slots[2].TagValue);                                   // cloned from slot 1
        Assert.Equal("4", slots[2].AmountField(SkillLevel.NoSkill)!.CurrentValue);
    }

    [Fact]
    public void An_add_to_an_empty_list_makes_one_empty_slot()
    {
        var slots = RecipeIngredients.WithListChanges(Array.Empty<IngredientSlot>(),
            new[] { new IngredientListChange(ChangeOperation.AddEntry, -1) });

        var only = Assert.Single(slots);
        Assert.Equal(0, only.Index);
        Assert.Null(only.Tag);
        Assert.Empty(only.Amounts);
    }

    [Fact]
    public void A_remove_drops_the_named_slot_and_renumbers_the_rest()
    {
        var slots = RecipeIngredients.WithListChanges(TwoSlots(),
            new[] { new IngredientListChange(ChangeOperation.RemoveEntry, 0) });

        var only = Assert.Single(slots);
        Assert.Equal(0, only.Index);
        Assert.Equal("CI_Metal_Scrap", only.TagValue);   // the survivor moved up to index 0
    }

    [Fact]
    public void A_remove_out_of_range_changes_nothing()
        => Assert.Equal(2, RecipeIngredients.WithListChanges(TwoSlots(),
            new[] { new IngredientListChange(ChangeOperation.RemoveEntry, 5) }).Count);

    [Fact]
    public void Changes_play_back_in_the_order_they_were_recorded()
    {
        // Remove index 0 twice: the first drop makes the old slot 1 the new slot 0, so the second
        // drop removes what was slot 1. Both go.
        var slots = RecipeIngredients.WithListChanges(TwoSlots(), new[]
        {
            new IngredientListChange(ChangeOperation.RemoveEntry, 0),
            new IngredientListChange(ChangeOperation.RemoveEntry, 0),
        });

        Assert.Empty(slots);
    }

    [Fact]
    public void An_added_slot_can_be_removed_again()
    {
        var slots = RecipeIngredients.WithListChanges(TwoSlots(), new[]
        {
            new IngredientListChange(ChangeOperation.AddEntry, -1),
            new IngredientListChange(ChangeOperation.RemoveEntry, 2),
        });

        Assert.Equal(new[] { 0, 1 }, slots.Select(s => s.Index).ToArray());
    }
}
