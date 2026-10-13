using SCMW.Core.Knowledge;
using Xunit;

namespace SCMW.Core.Tests;

/// <summary>
/// The three-band view. It must agree with FieldSafety on what is ordinary, split the grey-area
/// kinds (a name, a named choice) out as noted, and lock everything structural.
/// </summary>
public class ValueSafetyTests
{
    [Theory]
    [InlineData("Weight", "FloatProperty", ValueBand.Normal)]
    [InlineData("Weight", "ArrayProperty", ValueBand.Locked)]   // a familiar name, but a complex type
    [InlineData("Caption", "StructProperty", ValueBand.Locked)]
    [InlineData("MaxStackSize", "IntProperty", ValueBand.Normal)]
    [InlineData("Caption", "TextProperty", ValueBand.Locked)]    // SCUM text is a string-table key
    [InlineData("Description", "TextProperty", ValueBand.Locked)]
    [InlineData("IsSealed", "BoolProperty", ValueBand.Normal)]
    [InlineData("GridInventoryRowSpan", "ByteProperty", ValueBand.Normal)]   // known-safe byte
    [InlineData("_baseElementType", "NameProperty", ValueBand.Noted)]        // a named choice
    [InlineData("SomeEnumField", "EnumProperty", ValueBand.Noted)]
    [InlineData("SomeEnumField", "ByteProperty", ValueBand.Noted)]
    [InlineData("Ingredients[0].Amount.NoSkill", "IntProperty", ValueBand.Normal)]  // inside a list
    [InlineData("Ingredients[0].AllowedTypes[0]", "ObjectProperty", ValueBand.Noted)] // a retargetable tag
    [InlineData("Product", "SoftObjectProperty", ValueBand.Locked)]                  // an ordinary link
    [InlineData("_itemTags", "ArrayProperty", ValueBand.Locked)]
    [InlineData("StaticMesh", "ObjectProperty", ValueBand.Locked)]
    [InlineData("GridInventoryIcon", "SoftObjectProperty", ValueBand.Locked)]
    [InlineData("Mystery", null, ValueBand.Locked)]
    public void Sorts_fields_into_the_three_bands(string name, string? hint, ValueBand expected)
        => Assert.Equal(expected, ValueSafety.For(name, hint));

    [Fact]
    public void A_structural_name_is_locked_even_with_a_scalar_hint()
        => Assert.Equal(ValueBand.Locked, ValueSafety.For("GridInventoryIconIndex", "IntProperty"));
}
