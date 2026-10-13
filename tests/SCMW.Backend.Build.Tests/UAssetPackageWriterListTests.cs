using SCMW.Backend.Build;
using SCMW.Core.Editing;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The writer's list add/remove, tested without a game package (the same little name table the
/// other writer tests use). This is the code that only the env-gated real-install tests used to
/// touch, so it runs everywhere now.
/// </summary>
public class UAssetPackageWriterListTests
{
    private readonly FakeNameMap _names = new();

    private StructPropertyData Slot(int amount) =>
        new(_names.Name("Ingredients"))
        {
            Value = new List<PropertyData> { new IntPropertyData(_names.Name("NoSkill")) { Value = amount } },
        };

    private ArrayPropertyData Ingredients(params PropertyData[] slots) =>
        new(_names.Name("Ingredients")) { Value = slots };

    private static int Amount(PropertyData slot) =>
        ((IntPropertyData)((StructPropertyData)slot).Value[0]).Value;

    [Fact]
    public void Adding_an_entry_clones_the_last_one()
    {
        var array = Ingredients(Slot(4), Slot(5));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.Null(error);
        Assert.Equal(3, array.Value.Length);
        Assert.Equal(5, Amount(array.Value[2]));
        Assert.NotSame(array.Value[1], array.Value[2]);   // a real copy, not an alias
    }

    [Fact]
    public void Removing_an_entry_drops_the_named_one()
    {
        var array = Ingredients(Slot(4), Slot(5), Slot(6));

        Assert.True(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients[1]", "", ChangeOperation.RemoveEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.Null(error);
        Assert.Equal(2, array.Value.Length);
        Assert.Equal(4, Amount(array.Value[0]));
        Assert.Equal(6, Amount(array.Value[1]));
    }

    [Fact]
    public void Removing_an_out_of_range_entry_is_refused()
    {
        var array = Ingredients(Slot(4));

        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients[9]", "", ChangeOperation.RemoveEntry),
            out var found, out var error));

        Assert.True(found);                                // the list was found...
        Assert.False(string.IsNullOrWhiteSpace(error));    // ...but the entry was not
        Assert.Single(array.Value);
    }

    [Fact]
    public void A_field_that_is_not_a_list_is_not_found()
    {
        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { new IntPropertyData(_names.Name("Weight")) { Value = 3 } },
            new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.False(found);
    }

    [Fact]
    public void Adding_beyond_the_cap_is_refused()
    {
        var slots = Enumerable.Range(0, 32).Select(i => (PropertyData)Slot(i)).ToArray();
        var array = Ingredients(slots);

        Assert.False(UAssetPackageWriter.TryApplyListChange(
            new PropertyData[] { array }, new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            out var found, out var error));

        Assert.True(found);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(32, array.Value.Length);   // nothing was added
    }
}
