using SCMW.Backend.Pak;
using SCMW.Core;
using Xunit;

namespace SCMW.Backend.Pak.Tests;

/// <summary>
/// The pure half of the nested-value reader: given a little tree of property nodes (the shape
/// the UAssetAPI adapter produces), turn it into dotted-path rows. No game files, no library —
/// so this part is fast and always runs.
/// </summary>
public class NestedFieldWalkerTests
{
    private static NestedProperty Scalar(string name, string type, string? value = "1") =>
        new(name, type, value, Array.Empty<NestedProperty>());

    private static NestedProperty Struct(string name, params NestedProperty[] children) =>
        new(name, "StructProperty", null, children);

    [Fact]
    public void A_struct_inside_a_struct_becomes_one_dotted_row_per_value()
    {
        var nodes = new[]
        {
            Struct("_replenishableResourceDescription",
                Struct("PeriodicReplenishment",
                    Scalar("MaxAmount", "FloatProperty", "40"),
                    Scalar("ReplenishInterval", "FloatProperty", "7.2"),
                    Scalar("ReplenishAmount", "FloatProperty", "0.1"))),
            Scalar("_healthPoints", "IntProperty", "600"),
        };

        var fields = NestedFieldWalker.Walk(nodes);

        Assert.Equal(
            new[]
            {
                "_replenishableResourceDescription.PeriodicReplenishment.MaxAmount",
                "_replenishableResourceDescription.PeriodicReplenishment.ReplenishInterval",
                "_replenishableResourceDescription.PeriodicReplenishment.ReplenishAmount",
                "_healthPoints",
            },
            fields.Select(f => f.TechnicalName));

        var amount = fields[2];
        Assert.Equal("0.1", amount.CurrentValue);
        Assert.Equal("FloatProperty", amount.ValueTypeHint);
        Assert.Equal(StatusKind.ReadOnly, amount.Status);
        Assert.True(amount.Nested);
    }

    [Fact]
    public void A_value_with_no_value_and_no_children_is_skipped()
    {
        // An object/link node arrives with a null value and nothing to descend into — it is
        // not a plain value we can show, so it must not produce a row.
        var nodes = new[]
        {
            new NestedProperty("_itemClass", "ObjectProperty", null, Array.Empty<NestedProperty>()),
            Scalar("_healthPoints", "IntProperty", "600"),
        };

        var fields = NestedFieldWalker.Walk(nodes);

        Assert.Equal(new[] { "_healthPoints" }, fields.Select(f => f.TechnicalName));
    }

    [Fact]
    public void An_empty_struct_produces_no_row()
    {
        var nodes = new[] { Struct("_empty") };
        Assert.Empty(NestedFieldWalker.Walk(nodes));
    }

    [Fact]
    public void Depth_beyond_the_cap_is_not_walked()
    {
        // Build a chain deeper than the cap; the deepest leaf must not appear.
        var node = Scalar("leaf", "IntProperty", "9");
        for (var i = 0; i < 10; i++) node = Struct("s" + i, node);

        var fields = NestedFieldWalker.Walk(new[] { node }, maxDepth: 3);

        Assert.DoesNotContain(fields, f => f.TechnicalName.EndsWith("leaf", StringComparison.Ordinal));
    }

    [Fact]
    public void The_field_cap_stops_the_walk()
    {
        var nodes = Enumerable.Range(0, 50)
            .Select(i => Scalar("f" + i, "IntProperty", i.ToString())).ToArray();

        var fields = NestedFieldWalker.Walk(nodes, maxFields: 5);

        Assert.Equal(5, fields.Count);
    }
}
