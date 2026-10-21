using SCMW.Backend.Build;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

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

    /// <summary>A bare in-memory package: no name table, no imports (the same start ObjectLinkTests uses).</summary>
    private static UAsset NewPackage()
    {
        var asset = new UAsset(EngineVersion.VER_UE4_27);
        asset.ClearNameIndexList();
        asset.Imports = new List<Import>();
        return asset;
    }

    /// <summary>The real shape of an item's CraftingIngredientInstances, built on <paramref name="asset"/>.</summary>
    private static ArrayPropertyData TaggedList(UAsset asset, string field, string donorTag, out FPackageIndex tagIndex)
    {
        var package = asset.AddImport(new Import(
            "/Script/CoreUObject", "Package", FPackageIndex.FromRawIndex(0),
            $"/Game/ConZ_Files/Items/Crafting/Ingredients/{donorTag}", false, asset));
        tagIndex = asset.AddImport(new Import("/Script/SCUM", "CraftingIngredientTag", package, donorTag, false, asset));

        return new ArrayPropertyData(FName.FromString(asset, field))
        {
            ArrayType = FName.FromString(asset, "StructProperty"),
            Value = new PropertyData[]
            {
                new StructPropertyData(FName.FromString(asset, field),
                                       FName.FromString(asset, "CraftingIngredientInstance"))
                {
                    Value = new List<PropertyData>
                    {
                        new ObjectPropertyData(FName.FromString(asset, "Type")) { Value = tagIndex },
                        new FloatPropertyData(FName.FromString(asset, "Amount")) { Value = 1f },
                    },
                },
            },
        };
    }

    [Fact]
    public void Adding_a_tagged_list_points_it_at_the_new_category_in_its_own_package()
    {
        var donor = NewPackage();
        var shape = TaggedList(donor, "CraftingIngredientInstances", "CI_Group_Canned_Vegetables", out var donorTag);
        var target = NewPackage();
        var targetProperties = new List<PropertyData>();

        Assert.True(UAssetPackageWriter.TryInsertClonedTaggedList(
            targetProperties, shape, donor, "CraftingIngredientInstances",
            "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives", target,
            out var error), error);

        var added = Assert.IsType<ArrayPropertyData>(Assert.Single(targetProperties));
        var entry = Assert.IsType<StructPropertyData>(Assert.Single(added.Value));
        var type = Assert.IsType<ObjectPropertyData>(entry.Value.Single(p => p.Name.ToString() == "Type"));
        var amount = Assert.IsType<FloatPropertyData>(entry.Value.Single(p => p.Name.ToString() == "Amount"));

        Assert.Equal(1f, amount.Value);
        Assert.True(type.Value.IsImport());
        var tag = type.Value.ToImport(target);
        Assert.Equal("CI_Olives", tag.ObjectName.ToString());
        Assert.Equal("/Script/SCUM", tag.ClassPackage.ToString());
        Assert.True(tag.OuterIndex.IsImport());
        Assert.Equal("/Game/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives",
                     tag.OuterIndex.ToImport(target).ObjectName.ToString());

        // The donor keeps pointing at its own tag — the copy must stand alone.
        var donorEntry = (StructPropertyData)shape.Value[0];
        Assert.Equal(donorTag.Index,
            ((ObjectPropertyData)donorEntry.Value[0]).Value.Index);
    }

    [Fact]
    public void Adding_refuses_a_list_that_is_not_one_category_entry()
    {
        var donor = NewPackage();
        var empty = new ArrayPropertyData(FName.FromString(donor, "CraftingIngredientInstances"))
        {
            ArrayType = FName.FromString(donor, "StructProperty"),
            Value = Array.Empty<PropertyData>(),
        };
        var target = NewPackage();
        var targetProperties = new List<PropertyData>();

        Assert.False(UAssetPackageWriter.TryInsertClonedTaggedList(
            targetProperties, empty, donor, "CraftingIngredientInstances",
            "SCUM/Content/CI_Olives", target, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(targetProperties);
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
