using SCMW.Backend.Build;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

public class PropertyValueTests
{
    [Fact]
    public void An_enum_backed_byte_is_refused_rather_than_silently_dropped()
    {
        // UAssetAPI keeps an enum value in EnumValue (an FName); the byte Value is not serialized.
        // Setting Value would succeed and change nothing — so it must be refused.
        var property = new BytePropertyData(new FName()) { ByteType = BytePropertyType.FName };

        Assert.False(PropertyValue.TrySet(property, null, "SomeEnumValue", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void A_plain_byte_is_set()
    {
        var property = new BytePropertyData(new FName()) { ByteType = BytePropertyType.Byte };

        Assert.True(PropertyValue.TrySet(property, null, "7", out _));
        Assert.Equal(7, property.Value);
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
}
