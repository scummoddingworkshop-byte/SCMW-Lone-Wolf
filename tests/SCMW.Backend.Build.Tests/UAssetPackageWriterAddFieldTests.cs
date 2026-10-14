using SCMW.Backend.Build;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The writer's clone-and-insert for a field the file does not have yet, tested without a game
/// package: the donor's exact shape is copied under the target's own field name, and anything we
/// cannot write is refused so the built pak still loads.
/// </summary>
public class UAssetPackageWriterAddFieldTests
{
    private readonly FakeNameMap _names = new();

    [Fact]
    public void Adding_a_field_copies_the_donors_shape_under_the_targets_name()
    {
        var donor = new FloatPropertyData(_names.Name("_autoDestructTime")) { Value = 5f };
        var target = new List<PropertyData>();

        Assert.True(UAssetPackageWriter.TryInsertClonedField(
            target, donor, "_autoDestructTime", "0", _names, out var error));

        Assert.Null(error);
        var added = Assert.IsType<FloatPropertyData>(Assert.Single(target));
        Assert.Equal("_autoDestructTime", added.Name.ToString());
        Assert.Equal(0f, added.Value);          // the value set on the copy, not the donor's 5
        Assert.NotSame(donor, added);           // a real copy, not an alias
        Assert.Equal(5f, donor.Value);          // the donor is left exactly as it was
    }

    [Fact]
    public void Adding_a_field_keeps_the_donors_exact_type()
    {
        var donor = new DoublePropertyData(_names.Name("ShelfLife")) { Value = 42d };
        var target = new List<PropertyData>();

        Assert.True(UAssetPackageWriter.TryInsertClonedField(
            target, donor, "ShelfLife", "84", _names, out var error));

        Assert.Null(error);
        var added = Assert.IsType<DoublePropertyData>(Assert.Single(target));
        Assert.Equal(84d, added.Value);
    }

    [Fact]
    public void Adding_refuses_a_field_the_target_already_has()
    {
        var donor = new FloatPropertyData(_names.Name("Weight")) { Value = 5f };
        var target = new List<PropertyData> { new FloatPropertyData(_names.Name("Weight")) { Value = 1f } };

        Assert.False(UAssetPackageWriter.TryInsertClonedField(
            target, donor, "Weight", "9", _names, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Single(target);                  // nothing was added
    }

    [Fact]
    public void Adding_refuses_a_shape_we_cannot_write()
    {
        var donor = new StructPropertyData(_names.Name("SomeStruct"));
        var target = new List<PropertyData>();

        Assert.False(UAssetPackageWriter.TryInsertClonedField(
            target, donor, "SomeStruct", "1", _names, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(target);
    }

    [Fact]
    public void Adding_refuses_a_value_the_donors_shape_cannot_take()
    {
        var donor = new FloatPropertyData(_names.Name("_autoDestructTime")) { Value = 5f };
        var target = new List<PropertyData>();

        Assert.False(UAssetPackageWriter.TryInsertClonedField(
            target, donor, "_autoDestructTime", "not-a-number", _names, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(target);                   // nothing half-added
    }
}
