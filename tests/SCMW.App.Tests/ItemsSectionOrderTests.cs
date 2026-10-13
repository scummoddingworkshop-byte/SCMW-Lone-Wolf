namespace SCMW.App.Tests;

/// <summary>
/// The Items detail pane must show its sections — and the values inside each — in the SAME
/// order for every item, instead of the order the game's file happens to list them.
/// </summary>
public class ItemsSectionOrderTests
{
    [Fact]
    public void Sections_are_in_a_fixed_order_and_values_sort_within_a_section()
    {
        // Deliberately scrambled, as a real file might be.
        var values = new[]
        {
            new ItemValue("Zeta", "Other", "1"),
            new ItemValue("Beta", "Gameplay", "2"),
            new ItemValue("Weight", "Inventory", "3"),
            new ItemValue("Height", "Inventory", "4"),
            new ItemValue("Alpha", "Gameplay", "5"),
            new ItemValue("Gamma", "Appearance", "6"),
            new ItemValue("Nib", "ItemSettings", "7"),
        };

        var sections = ItemsViewModel.GroupSections(values);

        Assert.Equal(
            new[] { "Inventory", "Gameplay", "Appearance", "ItemSettings", "Other" },
            sections.Select(s => s.Name).ToArray());

        Assert.Equal(new[] { "Height", "Weight" }, sections[0].Values.Select(v => v.Name).ToArray());
        Assert.Equal(new[] { "Alpha", "Beta" }, sections[1].Values.Select(v => v.Name).ToArray());
    }

    [Fact]
    public void Ingredient_slots_keep_numeric_order()
    {
        var values = new[]
        {
            new ItemValue("Amount", "Ingredient 10", "1", technicalName: "Ingredients[9].Amount"),
            new ItemValue("Amount", "Ingredient 2", "1", technicalName: "Ingredients[1].Amount"),
            new ItemValue("Amount", "Ingredient 1", "1", technicalName: "Ingredients[0].Amount"),
        };

        var sections = ItemsViewModel.GroupSections(values);

        Assert.Equal(
            new[] { "Ingredient 1", "Ingredient 2", "Ingredient 10" },
            sections.Select(s => s.Name).ToArray());
    }
}
