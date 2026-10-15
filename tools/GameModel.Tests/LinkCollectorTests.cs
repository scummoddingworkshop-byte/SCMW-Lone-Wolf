using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.GameModel;

/// <summary>Turns the references in a family's fields into family-to-family links.</summary>
public class LinkCollectorTests
{
    private static AssetField F(string name, string value)
        => new(name, name, StatusKind.ReadOnly, value, "ObjectProperty");

    [Fact]
    public void Counts_references_and_distinct_source_assets()
    {
        var links = new LinkCollector();
        links.Add("base_elements", "A", new[] { F("_choppingRecipeAsset", "SCUM/Content/Chop_Items/Chop_X.Chop_X") });
        links.Add("base_elements", "A", new[] { F("_choppingRecipeAsset", "SCUM/Content/Chop_Items/Chop_Y.Chop_Y") });
        links.Add("base_elements", "B", new[] { F("_choppingRecipeAsset", "SCUM/Content/Chop_Items/Chop_X.Chop_X") });

        var row = links.Rows().Single();
        Assert.Equal("base_elements", row.From);
        Assert.Equal("chopping", row.To);
        Assert.Equal(3, row.Refs);
        Assert.Equal(2, row.AssetCount);
    }

    [Fact]
    public void Remembers_an_example_field()
    {
        var links = new LinkCollector();
        links.Add("recipes", "R", new[] { F("Ingredients[0].AllowedTypes[0]", "CI_Metal_Scrap") });

        Assert.Equal("Ingredients[0].AllowedTypes[0]", links.Rows().Single().ExampleField);
        Assert.Equal("CI_Metal_Scrap", links.Rows().Single().ExampleValue);
    }

    [Fact]
    public void Ignores_values_that_point_at_no_family()
    {
        var links = new LinkCollector();
        links.Add("items", "I", new[] { F("Weight", "24"), F("Caption", "Key Card") });

        Assert.Empty(links.Rows());
    }
}
