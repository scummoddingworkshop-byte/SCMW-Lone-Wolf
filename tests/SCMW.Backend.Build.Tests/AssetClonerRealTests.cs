using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The cloner's real job: open a copy of a real recipe, rename its own object(s) so the copy is a
/// distinct asset, and save. Skips when no game is configured. The registry writer's real test and
/// the in-game test cover the rest of the "new content" path.
/// </summary>
public class AssetClonerRealTests
{
    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real clone test.")]
    public void A_real_recipe_is_renamed_and_keeps_no_trace_of_the_donor()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string donor = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";
        Assert.True(mount.TryReadPackage(donor, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Stage the donor under the NEW name (as the build lays a clone out), then rename its object.
            foreach (var pair in files!)
                File.WriteAllBytes(Path.Combine(dir, CloneLayout.StagedFileName(pair.Key, "CR_1H_Stone_Axe", "CR_SECMW_Test")), pair.Value);

            var uasset = Directory.GetFiles(dir, "*.uasset").Single();
            Assert.True(new UAssetCloner().TryRenameObjects(uasset, "CR_1H_Stone_Axe", "CR_SECMW_Test",
                Array.Empty<AssetReferenceRemap>(), out var renameError), renameError);

            // The copy loads and its own object is the new name — nothing still claims the donor's.
            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var names = reloaded.Exports.Select(e => e.ObjectName.ToString()).ToList();
            Assert.Contains("CR_SECMW_Test", names);
            Assert.DoesNotContain("CR_1H_Stone_Axe", names);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real clone test.")]
    public void A_real_item_renames_its_object_class_and_default_object()
    {
        // An item is a Blueprint: its object, its generated class and that class's default object must
        // all move together, or the game loads a half-renamed asset and crashes.
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string donor = "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil";
        Assert.True(mount.TryReadPackage(donor, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-clone-item-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var pair in files!)
                File.WriteAllBytes(Path.Combine(dir, CloneLayout.StagedFileName(pair.Key, "VegetableOil", "OliveOilTEST")), pair.Value);

            var uasset = Directory.GetFiles(dir, "*.uasset").Single();
            Assert.True(new UAssetCloner().TryRenameObjects(uasset, "VegetableOil", "OliveOilTEST",
                Array.Empty<AssetReferenceRemap>(), out var renameError), renameError);

            var names = new UAsset(uasset, EngineVersion.VER_UE4_27).Exports
                .Select(e => e.ObjectName.ToString()).ToList();

            // An item package's top object IS its generated class; there is no plain-named export.
            Assert.Contains("OliveOilTEST_C", names);                  // its generated class
            Assert.Contains("Default__OliveOilTEST_C", names);         // that class's default object
            Assert.DoesNotContain("VegetableOil_C", names);
            Assert.DoesNotContain("Default__VegetableOil_C", names);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real clone test.")]
    public void A_real_item_ES_is_re_pointed_from_the_donor_item_to_the_copy()
    {
        // The "_ES" companion names its item in a soft reference (its "Actor" property). A copy that
        // keeps the donor's name makes the game bind the setup to the donor item, so the new item is
        // left with no setup at all: empty Examine, components that never attach, refused pickup.
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string donorFolder = "/Game/ConZ_Files/Items/Food/MiscFood";
        const string donorItem = donorFolder + "/VegetableOil";
        const string donorPakEs = "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil_ES";

        Assert.True(mount.TryReadPackage(donorPakEs, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-clone-es-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var pair in files!)
                File.WriteAllBytes(Path.Combine(dir, CloneLayout.StagedFileName(pair.Key, "VegetableOil_ES", "OliveOilTEST_ES")), pair.Value);

            var uasset = Directory.GetFiles(dir, "*.uasset").Single();
            var remaps = new[]
            {
                new AssetReferenceRemap(donorItem, donorFolder + "/SCMW_MyStoneAxeTEST/OliveOilTEST",
                    "VegetableOil", "OliveOilTEST"),
            };

            Assert.True(new UAssetCloner().TryRenameObjects(uasset, "VegetableOil_ES", "OliveOilTEST_ES",
                remaps, out var renameError), renameError);

            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var actor = reloaded.Exports.OfType<NormalExport>()
                .SelectMany(e => e.Data ?? new List<PropertyData>())
                .OfType<SoftObjectPropertyData>()
                .Select(p => p.Value.AssetPath.AssetName?.ToString())
                .ToList();

            Assert.DoesNotContain(donorItem + ".VegetableOil_C", actor);
            Assert.Contains(donorFolder + "/SCMW_MyStoneAxeTEST/OliveOilTEST.OliveOilTEST_C", actor);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real writer test.")]
    public void A_real_recipes_product_can_be_re_pointed_at_another_item()
    {
        // Making a recipe that produces a NEW item (Phase 8.2) relies on this: the writer must find a
        // real recipe's "Product" soft link and re-point it. Proven on the game's own files.
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string donor = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";
        Assert.True(mount.TryReadPackage(donor, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-product-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var pair in files!)
                File.WriteAllBytes(Path.Combine(dir, CloneLayout.StagedFileName(pair.Key, "CR_1H_Stone_Axe", "CR_SECMW_Test")), pair.Value);

            var uasset = Directory.GetFiles(dir, "*.uasset").Single();
            Assert.True(new UAssetCloner().TryRenameObjects(uasset, "CR_1H_Stone_Axe", "CR_SECMW_Test",
                Array.Empty<AssetReferenceRemap>(), out var renameError), renameError);

            var before = ProductOf(uasset);
            Assert.False(string.IsNullOrEmpty(before));   // a real recipe really does carry a "Product"

            const string target = "/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil_C";
            var written = new UAssetPackageWriter().Apply(uasset, new[] { new FieldChange("Product", target) });
            Assert.True(written.Ok, written.Error);

            Assert.Equal(target, ProductOf(uasset));
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real writer test.")]
    public void A_real_recipes_shelf_and_order_can_be_set()
    {
        // Phase 8.3 relies on this: the crafting group lives on the recipe's companion object, not
        // its main one, and the order is a byte. The writer must reach both and read them back.
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string donor = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";
        Assert.True(mount.TryReadPackage(donor, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-shelf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var pair in files!)
                File.WriteAllBytes(Path.Combine(dir, CloneLayout.StagedFileName(pair.Key, "CR_1H_Stone_Axe", "CR_SECMW_Shelf")), pair.Value);

            var uasset = Directory.GetFiles(dir, "*.uasset").Single();
            var written = new UAssetPackageWriter().Apply(uasset, new[]
            {
                new FieldChange("CraftingCategoryTag.TagName", "CraftingCategory.Items.Clothing.ClothingRepairs"),
                new FieldChange("DisplayPriority", "2"),
            });
            Assert.True(written.Ok, written.Error);

            Assert.Equal("CraftingCategory.Items.Clothing.ClothingRepairs",
                StructFieldOf(uasset, "CraftingCategoryTag", "TagName"));
            Assert.Equal("2", FieldOf(uasset, "DisplayPriority"));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A top-level value on any export of the package (the order lives beside the struct).</summary>
    private static string? FieldOf(string uasset, string fieldName)
    {
        var package = new UAsset(uasset, EngineVersion.VER_UE4_27);
        foreach (var export in package.Exports.OfType<NormalExport>())
        {
            foreach (var prop in export.Data ?? new List<PropertyData>())
            {
                if (prop.Name.ToString() != fieldName) continue;
                return ScalarOf(prop);
            }
        }

        return null;
    }

    /// <summary>A value inside a struct property (the shelf's tag lives in one).</summary>
    private static string? StructFieldOf(string uasset, string structName, string fieldName)
    {
        var package = new UAsset(uasset, EngineVersion.VER_UE4_27);
        foreach (var export in package.Exports.OfType<NormalExport>())
        {
            foreach (var prop in export.Data ?? new List<PropertyData>())
            {
                if (prop.Name.ToString() != structName || prop is not StructPropertyData structure)
                    continue;

                foreach (var child in structure.Value ?? new List<PropertyData>())
                {
                    if (child.Name.ToString() != fieldName) continue;
                    return ScalarOf(child);
                }
            }
        }

        return null;
    }

    /// <summary>A scalar property as the plain text the writer stored.</summary>
    private static string? ScalarOf(PropertyData property) => property switch
    {
        NamePropertyData name => name.Value?.ToString(),
        BytePropertyData b => b.Value.ToString(),
        IntPropertyData i => i.Value.ToString(),
        _ => null,
    };

    /// <summary>A recipe's "Product" value — the item it makes — as the writer stores it, or null.</summary>
    private static string? ProductOf(string uasset)
    {
        var package = new UAsset(uasset, EngineVersion.VER_UE4_27);
        foreach (var export in package.Exports.OfType<NormalExport>())
        {
            foreach (var prop in export.Data ?? new List<PropertyData>())
            {
                if (prop.Name.ToString() != "Product") continue;
                return prop is SoftObjectPropertyData soft ? soft.Value.AssetPath.AssetName?.ToString() : null;
            }
        }

        return null;
    }
}
