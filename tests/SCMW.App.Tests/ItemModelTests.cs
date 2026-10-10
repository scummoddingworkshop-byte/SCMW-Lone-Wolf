using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ItemModelTests
{
    [AvaloniaFact]
    public void A_number_value_rejects_non_numbers()
    {
        var value = new ItemValue("Max stack", "Inventory", "10");

        value.Draft = "abc";
        Assert.False(value.IsDraftValid);

        value.Draft = "25";
        Assert.True(value.IsDraftValid);
    }

    [AvaloniaFact]
    public void Boolean_and_text_kinds_validate_for_later()
    {
        var flag = new ItemValue("Can be dropped", "Flags", "true", ItemValueKind.Boolean);
        flag.Draft = "maybe";
        Assert.False(flag.IsDraftValid);
        flag.Draft = "false";
        Assert.True(flag.IsDraftValid);

        var text = new ItemValue("Display name", "General", "Duct Tape", ItemValueKind.Text);
        text.Draft = "  ";
        Assert.False(text.IsDraftValid);
        text.Draft = "Hello";
        Assert.True(text.IsDraftValid);
    }

    [AvaloniaFact]
    public void A_changed_value_is_detected_and_keeps_its_original()
    {
        var value = new ItemValue("Max stack", "Inventory", "10");

        Assert.False(value.IsChanged);
        Assert.Equal("10", value.OriginalValue);
        Assert.Equal("was 10", value.WasText);

        value.Value = "25";
        Assert.True(value.IsChanged);

        value.Value = "10";
        Assert.False(value.IsChanged);
    }

    [AvaloniaFact]
    public void An_item_exposes_its_details_and_values()
    {
        var item = new ItemSummary(
            "Duct Tape", "Tools", "Sticks things together", editable: true,
            values: new[] { new ItemValue("Max stack", "Inventory", "10") });

        Assert.Equal("Duct Tape", item.Name);
        Assert.Equal("Tools", item.Category);
        Assert.True(item.Editable);
        Assert.Single(item.Values);
    }

    [AvaloniaFact]
    public void Numbers_use_the_games_format_regardless_of_culture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var value = new ItemValue("Weight", "Weight", "0.5");

            value.Draft = "0.5";          // the game's dot form
            Assert.True(value.IsDraftValid);

            value.Draft = "0,5";          // not our format, even on a comma culture
            Assert.False(value.IsDraftValid);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
