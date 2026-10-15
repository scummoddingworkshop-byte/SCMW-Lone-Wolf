namespace SCMW.GameModel;

/// <summary>Turns a SCUM string table into notebook fields (its two real members).</summary>
public class StringTableFieldsTests
{
    [Fact]
    public void Reports_the_namespace_and_a_summary_of_the_entries()
    {
        var fields = StringTableFields.Build("UI_Crafting", new Dictionary<string, string>
        {
            ["OverlayTitle_Components"] = "Components",
            ["SubTabTitle_ITEMS"] = "Items",
        });

        var ns = fields.Single(f => f.TechnicalName == "StringTable.TableNamespace");
        Assert.Equal("UI_Crafting", ns.CurrentValue);

        var keys = fields.Single(f => f.TechnicalName == "StringTable.KeysToEntries");
        Assert.Contains("2 entries", keys.CurrentValue);
        Assert.Contains("OverlayTitle_Components", keys.CurrentValue);
    }

    [Fact]
    public void Says_zero_for_an_empty_table()
    {
        var fields = StringTableFields.Build("", new Dictionary<string, string>());

        var keys = fields.Single(f => f.TechnicalName == "StringTable.KeysToEntries");
        Assert.Equal("0 entries", keys.CurrentValue);
    }
}
