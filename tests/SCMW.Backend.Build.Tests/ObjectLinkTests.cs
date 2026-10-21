using SCMW.Backend.Build;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>Pointing an ingredient's accepted-tag link at a tag, and its import table entry.</summary>
public class ObjectLinkTests
{
    private static UAsset NewAsset()
    {
        var asset = new UAsset(EngineVersion.VER_UE4_27);
        asset.ClearNameIndexList();   // an in-memory asset starts with no name table
        asset.Imports = new List<Import>();
        return asset;
    }

    private static ObjectPropertyData Link(UAsset asset, FPackageIndex value) =>
        new(FName.FromString(asset, "AllowedTypes")) { Value = value };

    /// <summary>Adds a tag the way the game does: the tag object plus its own package import.</summary>
    private static FPackageIndex ImportTag(UAsset asset, string tag)
    {
        var package = asset.AddImport(new Import(
            "/Script/CoreUObject", "Package", FPackageIndex.FromRawIndex(0),
            $"/Game/ConZ_Files/Items/Crafting/Ingredients/{tag}", false, asset));
        return asset.AddImport(new Import("/Script/SCUM", "CraftingIngredientTag", package, tag, false, asset));
    }

    private static Import TagImport(UAsset asset, FPackageIndex link) => link.ToImport(asset);

    [Fact]
    public void A_tag_already_imported_is_reused_without_a_duplicate()
    {
        var asset = NewAsset();
        var rope = ImportTag(asset, "CI_Group_Rope");

        var link = Link(asset, rope);
        Assert.True(ObjectLink.TrySet(asset, link, "CI_Group_Rope", out var error), error);

        Assert.Equal(rope.Index, link.Value.Index);
        Assert.Equal(2, asset.Imports.Count);   // reused, not duplicated
    }

    [Fact]
    public void A_new_tag_gets_its_own_package_and_resolves_by_full_path()
    {
        var asset = NewAsset();
        var seed = ImportTag(asset, "CI_Group_Fastener");

        var link = Link(asset, seed);
        Assert.True(ObjectLink.TrySet(asset, link, "CI_Group_Logs_Small", out var error), error);

        var tag = TagImport(asset, link.Value);
        Assert.Equal("CI_Group_Logs_Small", tag.ObjectName.ToString());

        // The parent must be the tag's OWN package, not the package of the tag we copied from.
        Assert.True(tag.OuterIndex.IsImport());
        var package = tag.OuterIndex.ToImport(asset);
        Assert.Equal("Package", package.ClassName.ToString());
        Assert.Equal("/Game/ConZ_Files/Items/Crafting/Ingredients/CI_Group_Logs_Small",
                     package.ObjectName.ToString());
    }

    [Fact]
    public void A_whole_path_points_at_a_category_the_project_created()
    {
        var asset = NewAsset();
        var seed = ImportTag(asset, "CI_Group_Fastener");
        var link = Link(asset, seed);

        Assert.True(ObjectLink.TrySetNew(asset, link,
            "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives",
            seed.ToImport(asset), out var error), error);

        var tag = TagImport(asset, link.Value);
        Assert.Equal("CI_Olives", tag.ObjectName.ToString());
        Assert.Equal("/Game/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives",
                     tag.OuterIndex.ToImport(asset).ObjectName.ToString());
    }

    [Fact]
    public void A_new_food_link_is_written_into_the_soft_object()
    {
        var asset = NewAsset();
        var link = new SoftObjectPropertyData(FName.FromString(asset, "Item"))
        {
            Value = new FSoftObjectPath(
                FName.FromString(asset, "/Game"),
                FName.FromString(asset, "/Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C"),
                FString.FromString(string.Empty)),
        };

        Assert.True(ObjectLink.TrySetSoft(asset, link, "/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C",
                                          out var error), error);

        // The game keeps the whole object path in the asset name; the write must do the same.
        Assert.Equal("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C",
                     link.Value.AssetPath.AssetName.ToString());
    }
}
