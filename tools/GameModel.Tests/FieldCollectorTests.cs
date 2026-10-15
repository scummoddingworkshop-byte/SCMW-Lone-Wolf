using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.GameModel;

/// <summary>Collects one asset's fields into the family table (count, type, example).</summary>
public class FieldCollectorTests
{
    private static AssetField F(string name, string type = "FloatProperty", string value = "", bool nested = false)
        => new(name, name, StatusKind.ReadOnly, value, type, nested);

    [Fact]
    public void Counts_how_many_assets_carry_each_field()
    {
        var rows = FieldCollector.Collect(new[]
        {
            new[] { F("Weight"), F("MaxQuantity") },
            new[] { F("Weight") },
        });

        Assert.Equal(2, rows.Single(r => r.Name == "Weight").Assets);
        Assert.Equal(1, rows.Single(r => r.Name == "MaxQuantity").Assets);
    }

    [Fact]
    public void Sorts_by_asset_count_then_name()
    {
        var rows = FieldCollector.Collect(new[]
        {
            new[] { F("Zeta"), F("Alpha") },
            new[] { F("Alpha") },
        });

        Assert.Equal(new[] { "Alpha", "Zeta" }, rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Keeps_the_first_non_empty_example_value()
    {
        var rows = FieldCollector.Collect(new[]
        {
            new[] { F("Weight", value: "") },
            new[] { F("Weight", value: "20000") },
        });

        Assert.Equal("20000", rows.Single().Example);
    }

    [Fact]
    public void Carries_the_type_and_the_nested_flag()
    {
        var rows = FieldCollector.Collect(new[]
        {
            new[] { F("GridInventorySortGroup.TagName", type: "NameProperty", value: "ItemSortGroup.Other", nested: true) },
        });

        var row = rows.Single();
        Assert.Equal("NameProperty", row.Type);
        Assert.True(row.Nested);
    }

    [Fact]
    public void Does_not_double_count_a_repeated_field_within_one_asset()
    {
        var rows = FieldCollector.Collect(new[] { new[] { F("Weight"), F("Weight") } });

        Assert.Equal(1, rows.Single().Assets);
    }

    [Fact]
    public void Returns_nothing_for_no_assets()
        => Assert.Empty(FieldCollector.Collect(Array.Empty<IReadOnlyList<AssetField>>()));
}
