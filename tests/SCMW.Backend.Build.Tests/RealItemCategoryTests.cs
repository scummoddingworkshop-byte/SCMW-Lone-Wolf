using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Core.Editing;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The real job of "give an item an ingredient category" (Phase 8, Option A): take the game's own
/// fresh Olives item, which has no category list, add one pointing at a category, and read it back.
/// Also proves a category can be copied under a new name (the reusable "New category" tool).
/// </summary>
public class RealItemCategoryTests
{
    private const string Olives = "SCUM/Content/ConZ_Files/Items/Food/Fruit/Olives_ES";
    private const string CannedOlives = "SCUM/Content/ConZ_Files/Items/Food/Canned_Food/BlackOlives_ES";
    private const string Category = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Group_Canned_Vegetables";
    private const string NewCategory = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives";

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real writer test.")]
    public void A_real_item_can_be_given_an_ingredient_category()
    {
        using var mount = MountGame();
        var dir = TempDir("scmw-category-item-");
        try
        {
            var item = Extract(mount, Olives, dir);
            var donor = Extract(mount, CannedOlives, dir);

            var written = new UAssetPackageWriter().Apply(item,
                new[] { new FieldChange("CraftingIngredientInstances", NewCategory,
                                        ChangeOperation.AddField, CannedOlives) },
                new Dictionary<string, string> { ["CraftingIngredientInstances"] = donor });

            Assert.True(written.Ok, written.Error);

            var asset = new UAsset(item, EngineVersion.VER_UE4_27);
            var list = ListOf(asset);
            var entry = Assert.IsType<StructPropertyData>(Assert.Single(list.Value));
            var link = Assert.IsType<ObjectPropertyData>(
                entry.Value.Single(p => p.Name.ToString() == "Type"));
            var amount = Assert.IsType<FloatPropertyData>(
                entry.Value.Single(p => p.Name.ToString() == "Amount"));

            Assert.Equal(1f, amount.Value);
            Assert.True(link.Value.IsImport());
            Assert.Equal("CI_Olives", link.Value.ToImport(asset).ObjectName.ToString());
            Assert.Equal("/Game/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives",
                         link.Value.ToImport(asset).OuterIndex.ToImport(asset).ObjectName.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real clone test.")]
    public void A_real_category_is_cloned_under_its_own_name()
    {
        using var mount = MountGame();
        var dir = TempDir("scmw-category-clone-");
        try
        {
            var uasset = Extract(mount, Category, dir);

            Assert.True(new UAssetCloner().TryRenameObjects(uasset, "CI_Group_Canned_Vegetables", "CI_Olives",
                Array.Empty<AssetReferenceRemap>(), out var error), error);

            var names = new UAsset(uasset, EngineVersion.VER_UE4_27).Exports
                .Select(e => e.ObjectName.ToString()).ToList();

            // A category is a plain data asset (no generated "_C" class like an item).
            Assert.Contains("CI_Olives", names);
            Assert.DoesNotContain("CI_Group_Canned_Vegetables", names);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real writer test.")]
    public void A_real_recipes_ingredient_can_point_at_a_project_category()
    {
        // The other half of Option A: a recipe's ingredient slot must be able to accept the category
        // the project created. Its whole path is written, so it lands in the mod's own folder.
        const string recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";
        const string category = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives";

        using var mount = MountGame();
        var dir = TempDir("scmw-category-recipe-");
        try
        {
            var uasset = Extract(mount, recipe, dir);

            var written = new UAssetPackageWriter().Apply(uasset,
                new[] { new FieldChange("Ingredients[0].AllowedTypes[0]", category) });
            Assert.True(written.Ok, written.Error);

            var asset = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var link = asset.Exports.OfType<NormalExport>()
                .SelectMany(e => e.Data ?? new List<PropertyData>())
                .OfType<ArrayPropertyData>()
                .Single(p => p.Name.ToString() == "Ingredients")
                .Value.OfType<StructPropertyData>()
                .First()
                .Value.OfType<ArrayPropertyData>()
                .Single(p => p.Name.ToString() == "AllowedTypes")
                .Value.OfType<ObjectPropertyData>()
                .Single();

            Assert.True(link.Value.IsImport());
            Assert.Equal("CI_Olives", link.Value.ToImport(asset).ObjectName.ToString());
            Assert.Equal("/Game/ConZ_Files/Items/Crafting/Ingredients/SCMW_OlivePress/CI_Olives",
                         link.Value.ToImport(asset).OuterIndex.ToImport(asset).ObjectName.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }

    private static PakMount MountGame()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var mount = new PakMount();
        Assert.True(mount.TryMount(Path.Combine(gamePath, "SCUM", "Content", "Paks"), key, out var error), error);
        return mount;
    }

    private static string TempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes every file of one real package into <paramref name="dir"/> and returns its .uasset.</summary>
    private static string Extract(PakMount mount, string package, string dir)
    {
        Assert.True(mount.TryReadPackage(package, out var files));
        string? uasset = null;
        foreach (var pair in files!)
        {
            // Keep the package's own file name: the writer and the cloner find the default object by it.
            var path = Path.Combine(dir, Path.GetFileName(pair.Key));
            File.WriteAllBytes(path, pair.Value);
            if (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) uasset = path;
        }

        return Assert.IsType<string>(uasset);
    }

    /// <summary>The item's own category list, wherever it lives (the class default object first).</summary>
    private static ArrayPropertyData ListOf(UAsset asset) =>
        asset.Exports.OfType<NormalExport>()
            .SelectMany(e => e.Data ?? new List<PropertyData>())
            .OfType<ArrayPropertyData>()
            .Single(p => p.Name.ToString() == "CraftingIngredientInstances");
}
