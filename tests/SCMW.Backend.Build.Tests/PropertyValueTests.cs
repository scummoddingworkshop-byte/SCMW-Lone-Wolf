using SCMW.Backend.Build;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

public class PropertyValueTests
{
    [Fact]
    public void An_enum_backed_byte_sets_its_named_choice()
    {
        // A named choice (e.g. _baseElementType = EBaseElementType::Well) is kept in EnumValue
        // (an FName); the writer now sets it rather than refusing the whole build.
        var names = new FakeNameMap();
        var property = new BytePropertyData(names.Name("_baseElementType")) { ByteType = BytePropertyType.FName };

        Assert.True(PropertyValue.TrySet(property, names, "EBaseElementType::Well", out var error));
        Assert.Null(error);
        Assert.Equal("EBaseElementType::Well", property.EnumValue.ToString());
    }

    [Fact]
    public void A_plain_byte_is_set()    {
        var property = new BytePropertyData(new FName()) { ByteType = BytePropertyType.Byte };

        Assert.True(PropertyValue.TrySet(property, null, "7", out _));
        Assert.Equal(7, property.Value);
    }

    [Fact]
    public void A_full_enum_property_sets_its_named_choice()
    {
        // _baseElementType is a full EnumProperty; the stored value is the "Type::Entry" name,
        // exactly what the screen shows, so the writer stores the typed name as-is.
        var names = new FakeNameMap();
        var property = new EnumPropertyData(names.Name("_baseElementType"));

        Assert.True(PropertyValue.TrySet(property, names, "EBaseElementType::Well", out var error));
        Assert.Null(error);
        Assert.Equal("EBaseElementType::Well", property.Value.ToString());
    }

    [Fact]
    public void A_float_that_is_not_a_number_is_refused_clearly()
    {
        var property = new FloatPropertyData(new FName());

        Assert.False(PropertyValue.TrySet(property, null, "abc", out var error));
        Assert.Contains("number", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_float_number_is_set_invariantly()
    {
        var property = new FloatPropertyData(new FName());

        Assert.True(PropertyValue.TrySet(property, null, "1.5", out _));
        Assert.Equal(1.5f, property.Value, 3);
    }

    [Fact]
    public void A_text_value_is_set()
    {
        // A base element's display name (_name) is a TextProperty; the app offers it, so the writer
        // must set it rather than refuse the whole build.
        var property = new TextPropertyData(new FName());

        Assert.True(PropertyValue.TrySet(property, null, "Wooden Crate", out var error));
        Assert.Null(error);
        Assert.Equal("Wooden Crate", property.Value.ToString());
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("no", false)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    public void A_switch_accepts_yes_no_and_true_false(string raw, bool expected)
    {
        // The screen shows a switch as "Yes"/"No", so the writer must accept that too.
        var property = new BoolPropertyData(new FName());

        Assert.True(PropertyValue.TrySet(property, null, raw, out _));
        Assert.Equal(expected, property.Value);
    }
}
