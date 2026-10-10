using SCMW.Core.Knowledge;
using Xunit;

public class FieldSafetyTests
{
    [Theory]
    [InlineData("Weight", "FloatProperty", EditSafety.Safe)]
    [InlineData("MaxQuantity", "IntProperty", EditSafety.Safe)]
    [InlineData("Caption", "TextProperty", EditSafety.Safe)]
    [InlineData("Description", "TextProperty", EditSafety.Safe)]
    [InlineData("IsSealed", "BoolProperty", EditSafety.Safe)]
    [InlineData("GridInventoryRowSpan", "ByteProperty", EditSafety.Safe)]   // known-safe byte
    [InlineData("GridInventoryColumnSpan", "ByteProperty", EditSafety.Safe)]
    [InlineData("SomeUnknownScalar", "FloatProperty", EditSafety.Safe)]     // plain scalar, benign name
    [InlineData("UserClasses", "ArrayProperty", EditSafety.Unsafe)]
    [InlineData("Components", "ArrayProperty", EditSafety.Unsafe)]
    [InlineData("GridInventorySortGroup", "StructProperty", EditSafety.Unsafe)]
    [InlineData("Actor", "SoftObjectProperty", EditSafety.Unsafe)]
    [InlineData("GridInventoryIcon", "SoftObjectProperty", EditSafety.Unsafe)]
    [InlineData("ExpirationTimeOverrideHours", "MapProperty", EditSafety.Unsafe)]
    [InlineData("SomeEnumField", "ByteProperty", EditSafety.Unsafe)]        // unknown byte: conservative
    [InlineData("SomeName", "NameProperty", EditSafety.Unsafe)]             // identifiers: conservative
    [InlineData("Mystery", null, EditSafety.Unsafe)]
    [InlineData("", null, EditSafety.Unsafe)]
    public void Classifies_fields_conservatively(string name, string? hint, EditSafety expected)
        => Assert.Equal(expected, FieldSafety.For(name, hint));

    [Fact]
    public void A_structural_name_is_unsafe_even_with_a_scalar_hint()
        => Assert.Equal(EditSafety.Unsafe, FieldSafety.For("GridInventoryIconIndex", "IntProperty"));
}
