using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Core.Editing;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The real job of naming a new item: take the game's own Vegetable Oil, give it the words "Olive Oil"
/// and a description of its own, and point its three pictures at another item's image. Reads back.
/// </summary>
public class RealItemTextTests
{
    private const string VegetableOil = "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil_ES";
    private const string OlivePicture =
        "/Game/ConZ_Files/Models/Objects/Items/Crafting_Items/Olives/ICO_Olives.ICO_Olives";

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real writer test.")]
    public void A_real_items_name_description_and_picture_can_be_changed()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;

        using var mount = new PakMount();
        Assert.True(mount.TryMount(Path.Combine(gamePath, "SCUM", "Content", "Paks"), key, out var error), error);
        Assert.True(mount.TryReadPackage(VegetableOil, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string? uasset = null;
            foreach (var pair in files!)
            {
                var path = Path.Combine(dir, Path.GetFileName(pair.Key));
                File.WriteAllBytes(path, pair.Value);
                if (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) uasset = path;
            }

            var written = new UAssetPackageWriter().Apply(Assert.IsType<string>(uasset), new[]
            {
                new FieldChange("Caption", "Olive Oil"),
                new FieldChange("Description", "Oil pressed from olives."),
                new FieldChange("GeneralPurposeIcon", OlivePicture),
                new FieldChange("GridInventoryIcon", OlivePicture),
                new FieldChange("HandsAndShouldersIcon", OlivePicture),
            });
            Assert.True(written.Ok, written.Error);

            var asset = new UAsset(uasset!, EngineVersion.VER_UE4_27);
            var props = asset.Exports.OfType<NormalExport>()
                .SelectMany(e => e.Data ?? new List<PropertyData>()).ToList();

            var caption = Assert.IsType<TextPropertyData>(props.Single(p => p.Name.ToString() == "Caption"));
            Assert.Equal(TextHistoryType.Base, caption.HistoryType);
            Assert.Equal("Olive Oil", caption.CultureInvariantString.ToString());

            // The donor's description is a string-table lookup: it must become plain words of its own.
            var description = Assert.IsType<TextPropertyData>(
                props.Single(p => p.Name.ToString() == "Description"));
            Assert.Equal(TextHistoryType.Base, description.HistoryType);
            Assert.Equal("Oil pressed from olives.", description.CultureInvariantString.ToString());

            foreach (var icon in new[] { "GeneralPurposeIcon", "GridInventoryIcon", "HandsAndShouldersIcon" })
            {
                var link = Assert.IsType<SoftObjectPropertyData>(
                    props.Single(p => p.Name.ToString() == icon));
                Assert.Equal(OlivePicture, link.Value.AssetPath.AssetName.ToString());
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
