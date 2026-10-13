using SCMW.Backend.Build;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The writer's path walk: a plain name picks a top-level property, a dotted path descends
/// through structs to the value hidden inside. Built from the same little name table the writer's
/// other tests use, so no game package is needed.
/// </summary>
public class PropertyPathTests
{
    private readonly FakeNameMap _names = new();

    private StructPropertyData Struct(string name, params PropertyData[] children) =>
        new(_names.Name(name)) { Value = children.ToList() };

    private FloatPropertyData Float(string name, float value) =>
        new(_names.Name(name)) { Value = value };

    private IntPropertyData Int(string name, int value) =>
        new(_names.Name(name)) { Value = value };

    private ArrayPropertyData Array(string name, params PropertyData[] elements) =>
        new(_names.Name(name)) { Value = elements };

    [Fact]
    public void A_plain_name_resolves_to_the_top_level_property()
    {
        var properties = new PropertyData[] { Float("_healthPoints", 600f) };

        Assert.Same(properties[0], PropertyPath.Resolve(properties, "_healthPoints"));
    }

    [Fact]
    public void A_dotted_path_walks_into_structs_to_the_leaf()
    {
        var leaf = Float("ReplenishAmount", 0.1f);
        var properties = new PropertyData[]
        {
            Struct("_replenishableResourceDescription",
                Struct("PeriodicReplenishment",
                    Float("MaxAmount", 40f),
                    leaf)),
        };

        var found = PropertyPath.Resolve(properties,
            "_replenishableResourceDescription.PeriodicReplenishment.ReplenishAmount");

        Assert.Same(leaf, found);
    }

    [Fact]
    public void A_missing_step_resolves_to_nothing()
    {
        var properties = new PropertyData[] { Struct("A", Float("B", 1f)) };

        Assert.Null(PropertyPath.Resolve(properties, "A.C"));
        Assert.Null(PropertyPath.Resolve(properties, "Z"));
    }

    [Fact]
    public void A_step_through_a_non_struct_resolves_to_nothing()
    {
        var properties = new PropertyData[] { Float("A", 1f) };

        Assert.Null(PropertyPath.Resolve(properties, "A.B"));
    }

    [Fact]
    public void A_list_step_reaches_a_value_inside_a_list_entry()
    {
        var leaf = Int("Basic", 3);
        var slot = Struct("Ingredients", Struct("Amount", leaf));
        var properties = new PropertyData[] { Array("Ingredients", slot) };

        Assert.Same(leaf, PropertyPath.Resolve(properties, "Ingredients[0].Amount.Basic"));
    }

    [Fact]
    public void A_list_index_out_of_range_resolves_to_nothing()
    {
        var properties = new PropertyData[] { Array("Ingredients", Struct("Amount", Int("Basic", 3))) };

        Assert.Null(PropertyPath.Resolve(properties, "Ingredients[5].Amount.Basic"));
    }

    [Fact]
    public void An_index_on_a_non_list_resolves_to_nothing()
    {
        var properties = new PropertyData[] { Float("A", 1f) };

        Assert.Null(PropertyPath.Resolve(properties, "A[0]"));
    }

    [Theory]
    [InlineData("Ingredients[0]", "Ingredients", 0)]
    [InlineData("Ingredients", "Ingredients", -1)]
    [InlineData("[2]", "", 2)]
    [InlineData("Weird[abc]", "Weird[abc]", -1)]
    public void A_step_splits_into_a_name_and_an_index(string step, string name, int index)
    {
        var (parsedName, parsedIndex) = PropertyPath.SplitStep(step);
        Assert.Equal(name, parsedName);
        Assert.Equal(index, parsedIndex);
    }

    [Theory]
    [InlineData("a.B.c", 3)]
    [InlineData("plain", 1)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a..b", 2)]   // empty steps are skipped
    [InlineData(".a.", 1)]
    public void Steps_split_on_dots_and_skip_empties(string path, int expected) =>
        Assert.Equal(expected, PropertyPath.Steps(path).Count);
}
