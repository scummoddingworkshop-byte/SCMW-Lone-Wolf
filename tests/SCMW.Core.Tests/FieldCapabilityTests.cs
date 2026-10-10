// tests/SCMW.Core.Tests/FieldCapabilityTests.cs
// Task 3: conservative capability rules for representative SCUM names and type hints.
using SCMW.Core.Knowledge;

namespace SCMW.Core.Tests;

public class FieldCapabilityTests
{
    [Theory] // known simple scalar names are writable without a type hint
    [InlineData("MaxStackSize")]
    [InlineData("Weight")]
    [InlineData("GridInventoryRowSpan")]
    [InlineData("GridInventoryColumnSpan")]
    [InlineData("ItemRarity")]
    [InlineData("Damage")]
    [InlineData("HealAmount")]
    [InlineData("ItemName")]
    [InlineData("DisplayName")]
    [InlineData("CamoPattern")]
    [InlineData("Durability")]
    [InlineData("SpawnRate")]
    public void Known_scalar_names_are_writable(string name)
    {
        Assert.Equal(WriteCapability.Writable, FieldCapability.For(name, null));
    }

    [Theory] // names match case-insensitively
    [InlineData("maxstacksize", WriteCapability.Writable)]
    [InlineData("DAMAGE", WriteCapability.Writable)]
    [InlineData("staticmesh", WriteCapability.NotWritable)]
    [InlineData("MATERIAL", WriteCapability.NotWritable)]
    public void Name_matching_is_case_insensitive(string name, WriteCapability expected)
    {
        Assert.Equal(expected, FieldCapability.For(name, null));
    }

    [Theory] // structural names/references are not writable
    [InlineData("StaticMesh")]
    [InlineData("SkeletalMesh")]
    [InlineData("Material")]
    [InlineData("BodyTexture")]
    [InlineData("GridInventoryIcon")]
    [InlineData("ItemClass")]
    [InlineData("Visual")]
    [InlineData("UseAnimation")]
    [InlineData("SpawnerPresets")]
    [InlineData("ImportData")]
    [InlineData("ThumbnailInfo")]
    public void Structural_names_are_not_writable(string name)
    {
        Assert.Equal(WriteCapability.NotWritable, FieldCapability.For(name, null));
    }

    [Theory] // unknown names with no type hint stay honestly Unknown
    [InlineData("SomeUnknownThing")]
    [InlineData("MysteryField")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unknown_names_without_a_hint_are_unknown(string? name)
    {
        Assert.Equal(WriteCapability.Unknown, FieldCapability.For(name!, null));
    }

    [Theory] // an unrecognised name is writable only because the type is a known scalar
    [InlineData("IntProperty")]
    [InlineData("FloatProperty")]
    [InlineData("BoolProperty")]
    [InlineData("StrProperty")]
    [InlineData("NameProperty")]
    [InlineData("EnumProperty")]
    [InlineData("ByteProperty")]
    [InlineData("int")]
    [InlineData("float")]
    [InlineData("bool")]
    [InlineData("string")]
    [InlineData("FName")]
    [InlineData("FString")]
    public void Scalar_type_hints_make_an_unknown_name_writable(string hint)
    {
        Assert.Equal(WriteCapability.Writable, FieldCapability.For("FooBar", hint));
    }

    [Theory] // complex type hints outrank everything
    [InlineData("ArrayProperty")]
    [InlineData("StructProperty")]
    [InlineData("ObjectProperty")]
    [InlineData("ClassProperty")]
    [InlineData("MapProperty")]
    [InlineData("StaticMeshProperty")]
    [InlineData("Texture2D")]
    [InlineData("TArray<int>")]
    public void Complex_type_hints_are_not_writable(string hint)
    {
        Assert.Equal(WriteCapability.NotWritable, FieldCapability.For("FooBar", hint));
    }

    [Theory] // a complex hint overrides an otherwise-writable scalar name
    [InlineData("MaxStackSize", "ArrayProperty")]
    [InlineData("Weight", "StructProperty")]
    [InlineData("Damage", "ObjectProperty")]
    public void Complex_type_hint_overrides_a_scalar_name(string name, string hint)
    {
        Assert.Equal(WriteCapability.NotWritable, FieldCapability.For(name, hint));
    }

    [Fact] // a structural name stays not-writable even with a scalar hint
    public void Structural_name_beats_a_scalar_hint()
    {
        Assert.Equal(WriteCapability.NotWritable, FieldCapability.For("StaticMesh", "IntProperty"));
    }

    [Fact] // a known scalar name plus a scalar hint is still writable
    public void Scalar_name_with_scalar_hint_stays_writable()
    {
        Assert.Equal(WriteCapability.Writable, FieldCapability.For("MaxStackSize", "IntProperty"));
    }

    [Fact] // an unrecognised hint on an unrecognised name stays Unknown (no guesses)
    public void Unrecognised_hint_on_an_unknown_name_is_unknown()
    {
        Assert.Equal(WriteCapability.Unknown, FieldCapability.For("MysteryField", "WeirdType"));
    }
}
