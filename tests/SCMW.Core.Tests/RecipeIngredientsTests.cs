using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The pure per-slot model of a recipe's ingredients: take the nested <c>Ingredients[N]…</c> fields
/// the reader returns and group them into one slot per index, each carrying the tag it accepts and
/// the amount it needs at every skill tier. No UI, no files — plain data in, plain data out.
/// </summary>
public class RecipeIngredientsTests
{
    /// <summary>One nested field as the reader emits it: friendly name = technical dotted path.</summary>
    private static AssetField F(string path, string value) =>
        new(path, path, StatusKind.ReadOnly, value, "IntProperty", Nested: true);

    [Fact]
    public void No_fields_yields_no_slots()
        => Assert.Empty(RecipeIngredients.From(Array.Empty<AssetField>()));

    [Fact]
    public void A_null_field_list_yields_no_slots()
        => Assert.Empty(RecipeIngredients.From(null));

    [Fact] // ExperienceReward.NoSkill also ends in ".NoSkill" but is not an ingredient slot
    public void A_field_that_is_not_an_ingredient_is_ignored()
    {
        var fields = new[]
        {
            F("Caption", "Garden"),
            F("ExperienceReward.NoSkill", "5"),
            F("ExperienceReward.AboveAdvanced", "20"),
        };

        Assert.Empty(RecipeIngredients.From(fields));
    }

    [Fact]
    public void The_five_amounts_are_read_by_skill_tier()
    {
        var fields = new[]
        {
            F("Ingredients[0].Amount.NoSkill", "13"),
            F("Ingredients[0].Amount.Basic", "12"),
            F("Ingredients[0].Amount.Medium", "11"),
            F("Ingredients[0].Amount.Advanced", "10"),
            F("Ingredients[0].Amount.AboveAdvanced", "9"),
        };

        var slot = Assert.Single(RecipeIngredients.From(fields));

        Assert.Equal("13", slot.AmountField(SkillLevel.NoSkill)!.CurrentValue);
        Assert.Equal("12", slot.AmountField(SkillLevel.Basic)!.CurrentValue);
        Assert.Equal("11", slot.AmountField(SkillLevel.Medium)!.CurrentValue);
        Assert.Equal("10", slot.AmountField(SkillLevel.Advanced)!.CurrentValue);
        Assert.Equal("9", slot.AmountField(SkillLevel.AboveAdvanced)!.CurrentValue);
    }

    [Fact]
    public void The_accepted_tag_is_read()
    {
        var slot = Assert.Single(RecipeIngredients.From(new[]
        {
            F("Ingredients[0].AllowedTypes[0]", "CI_Metal_Scrap"),
        }));

        Assert.Equal("CI_Metal_Scrap", slot.Tag!.CurrentValue);
    }

    [Fact]
    public void A_slot_with_only_a_tag_is_still_a_slot()
    {
        var slot = Assert.Single(RecipeIngredients.From(new[]
        {
            F("Ingredients[1].AllowedTypes[0]", "CI_Plank"),
        }));

        Assert.Equal(1, slot.Index);
        Assert.Null(slot.AmountField(SkillLevel.NoSkill));
    }

    [Fact]
    public void The_first_of_several_accepted_tags_is_the_slots_tag()
    {
        var slot = Assert.Single(RecipeIngredients.From(new[]
        {
            F("Ingredients[0].AllowedTypes[0]", "CI_Plank"),
            F("Ingredients[0].AllowedTypes[1]", "CI_Metal_Scrap"),
        }));

        Assert.Equal("CI_Plank", slot.Tag!.CurrentValue);
    }

    [Fact] // AdditionalAmount/DamagePercentageOnUncraft are different values, not the five amounts
    public void Related_but_different_amounts_are_not_the_five_skill_amounts()
    {
        var slot = Assert.Single(RecipeIngredients.From(new[]
        {
            F("Ingredients[0].AdditionalAmount.NoSkill", "3"),
            F("Ingredients[0].DamagePercentageOnUncraft.NoSkill", "1"),
        }));

        Assert.Empty(slot.Amounts);
    }

    [Fact] // the UI shows the five amounts in a fixed order; skills come back in that order
    public void The_amounts_come_back_in_skill_order()
    {
        var slot = Assert.Single(RecipeIngredients.From(new[]
        {
            F("Ingredients[0].Amount.Medium", "11"),
            F("Ingredients[0].Amount.NoSkill", "13"),
        }));

        Assert.Equal(new[] { SkillLevel.NoSkill, SkillLevel.Medium },
            slot.Amounts.Select(a => a.Level).ToArray());
    }

    [Fact] // the game file itself is the source of truth; a repeated path keeps the first row
    public void A_duplicate_amount_keeps_the_first()
    {
        var slot = Assert.Single(RecipeIngredients.From(new[]
        {
            F("Ingredients[0].Amount.NoSkill", "13"),
            F("Ingredients[0].Amount.NoSkill", "99"),
        }));

        Assert.Equal("13", slot.AmountField(SkillLevel.NoSkill)!.CurrentValue);
    }

    [Fact]
    public void Slots_are_ordered_by_index_even_when_the_fields_are_shuffled()
    {
        var slots = RecipeIngredients.From(new[]
        {
            F("Ingredients[1].Amount.NoSkill", "2"),
            F("Ingredients[0].Amount.NoSkill", "13"),
        });

        Assert.Equal(new[] { 0, 1 }, slots.Select(s => s.Index).ToArray());
    }

    // --- The write paths (the deliberate exception to the field-library rule) ---------------------
    // A recipe's ingredient values are per-slot paths the fixed library cannot express, so these
    // helpers are the ONE place that names them; the writer already resolves exactly these paths.

    [Theory]
    [InlineData(SkillLevel.NoSkill, "NoSkill")]
    [InlineData(SkillLevel.Basic, "Basic")]
    [InlineData(SkillLevel.Medium, "Medium")]
    [InlineData(SkillLevel.Advanced, "Advanced")]
    [InlineData(SkillLevel.AboveAdvanced, "AboveAdvanced")]
    public void A_skill_level_uses_the_games_own_field_name(SkillLevel level, string name)
        => Assert.Equal(name, RecipeIngredients.SkillName(level));

    [Fact]
    public void The_slot_tag_and_amount_paths_rebuild_the_files_own_names()
    {
        Assert.Equal("Ingredients[2]", RecipeIngredients.SlotPath(2));
        Assert.Equal("Ingredients[2].AllowedTypes[0]", RecipeIngredients.TagPath(2));
        Assert.Equal("Ingredients[2].Amount.Basic", RecipeIngredients.AmountPath(2, SkillLevel.Basic));
    }

    [Fact]
    public void The_slot_index_can_be_read_back_from_a_saved_path()
    {
        Assert.True(RecipeIngredients.TrySlotIndex("Ingredients[3]", out var bare));
        Assert.Equal(3, bare);

        Assert.True(RecipeIngredients.TrySlotIndex("Ingredients[3].Amount.Basic", out var nested));
        Assert.Equal(3, nested);

        Assert.False(RecipeIngredients.TrySlotIndex("Caption", out _));
    }
}
