using SCMW.Core;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

/// <summary>
/// The one list Build and Projects both read must keep a fixed order, so two items can be compared
/// line for line. These tests pin that order.
/// </summary>
public class ChangeDiffTests
{
    private static ChangeRecord Rec(string path, string field, string oldValue, string newValue,
                                    DateTime? at = null) =>
        new(Guid.NewGuid(), "p", path, field, oldValue, newValue,
            at ?? DateTime.UtcNow, StatusKind.Editable, AssetPath: path);

    [Fact] // values sit alphabetically on every card, not in the order they were changed
    public void Value_lines_are_alphabetical()
    {
        var items = ChangeDiff.Build(new[]
        {
            Rec("/g/Zeta_ES", "Max stack", "10", "25"),
            Rec("/g/Zeta_ES", "GridInventoryRowSpan", "2", "3"),
        });

        var values = Assert.Single(items).Values;
        Assert.Equal(new[] { "Height in grid", "Max Stack" },
                     values.Select(v => v.ValueName));
    }

    [Fact] // a recipe's ingredients stay together: every "what it accepts" first, then the amounts
    public void Recipe_ingredient_rows_group_by_ingredient()
    {
        var items = ChangeDiff.Build(new[]
        {
            Rec("/g/CR_Soap", "Product", "old", "new"),
            Rec("/g/CR_Soap", "Ingredients[2].Amount.AboveAdvanced", "2", "1"),
            Rec("/g/CR_Soap", "Ingredients[2].AllowedTypes[0]", "CI_A", "CI_B"),
            Rec("/g/CR_Soap", "Ingredients[0].AllowedTypes[0]", "CI_C", "CI_D"),
            Rec("/g/CR_Soap", "Ingredients[2].Amount.NoSkill", "10", "1"),
            Rec("/g/CR_Soap", "Ingredients[1].AllowedTypes[0]", "CI_E", "CI_F"),
            Rec("/g/CR_Soap", "Ingredients[2].Amount.Basic", "8", "1"),
        });

        Assert.Equal(new[]
        {
            "Ingredients[0].AllowedTypes[0]",
            "Ingredients[1].AllowedTypes[0]",
            "Ingredients[2].AllowedTypes[0]",
            "Ingredients[2].Amount.NoSkill",
            "Ingredients[2].Amount.Basic",
            "Ingredients[2].Amount.AboveAdvanced",
            "Product",
        }, Assert.Single(items).Values.Select(v => v.FieldTechnical));
    }

    [Fact] // item cards sit alphabetically too, so an item is always in the same place
    public void Item_cards_are_alphabetical()
    {
        var items = ChangeDiff.Build(new[]
        {
            Rec("/g/Zeta_ES", "Weight", "5", "3"),
            Rec("/g/Alpha_ES", "Weight", "4", "2"),
        });

        Assert.Equal(new[] { "/g/Alpha_ES", "/g/Zeta_ES" }, items.Select(i => i.ItemName));
    }
}
