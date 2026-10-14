using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

public class RealBuildIntegrationTests
{
    private sealed class FixedResolver : IAssetResolver
    {
        private readonly string _path;
        public FixedResolver(string path) => _path = path;
        public Task<AssetResolution> ResolveAsync(string name, CancellationToken ct)
            => Task.FromResult(new AssetResolution(_path, null));
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public async Task A_real_editable_item_is_written_packed_and_listed_back()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        // Discover a real editable (_ES) item that we can actually read, and one float field to set.
        var reader = new PakMountPackageReader(mount);
        var (package, files) = FindReadableEditableItem(mount, reader);
        var field = FirstFloatField(files);

        var root = Path.Combine(Path.GetTempPath(), "scmw-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repak = Path.Combine(AppContext.BaseDirectory, "tools", "repak", "repak.exe");
            var builder = new ModBuilder(reader, new UAssetPackageWriter(),
                new RepakPacker(repak, new SystemProcessRunner()),
                new FixedResolver(package), Path.Combine(root, "Builds"));

            var request = new BuildRequest("RealProof", "1.0.0",
                new[] { new BuildItem(package, new[] { new FieldChange(field.Field, field.NewValue) }) });

            var outcome = await builder.BuildAsync(request, null, CancellationToken.None);
            Assert.True(outcome.Ok, outcome.Error);
            Assert.True(File.Exists(outcome.PakPath));

            // The pak really contains the changed package on the game's own tree.
            var listed = await new SystemProcessRunner().RunAsync(repak,
                new[] { "list", outcome.PakPath! }, CancellationToken.None);
            Assert.Contains(package.Replace('\\', '/') + ".uasset", listed.Output.Replace('\\', '/'));

            // The writer's output still loads and holds the new value (round-trip on a scratch copy).
            var scratch = Path.Combine(root, "pkg");
            Directory.CreateDirectory(scratch);
            foreach (var pair in files!) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();
            Assert.True(new UAssetPackageWriter()
                .Apply(uasset, new[] { new FieldChange(field.Field, field.NewValue) }).Ok);

            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var again = reloaded.Exports.OfType<NormalExport>()
                .SelectMany(e => e.Data).OfType<FloatPropertyData>()
                .First(p => p.Name.ToString() == field.Field);
            Assert.Equal(
                float.Parse(field.NewValue, System.Globalization.CultureInfo.InvariantCulture),
                again.Value, 3);
        }
        finally { Directory.Delete(root, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public async Task A_component_field_maxquantity_is_readable_and_writable()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        var work = Path.Combine(Path.GetTempPath(), "scmw-comp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = new PakContentSource(new PakMount(), new AssetIndex(),
                Path.Combine(work, "a.db"), new FileIconCache(Path.Combine(work, "icons")));
            source.Configure(paks, key, null);
            Assert.Equal(ContentSourceState.Ready, (await source.ScanAsync()).State);

            // Find a real editable item that carries MaxQuantity on one of its component objects.
            var package = await FindItemWithField(source, "MaxQuantity");
            Assert.NotNull(package);

            using var mount = new PakMount();
            Assert.True(mount.TryMount(paks, key, out var error), error);
            Assert.True(mount.TryReadPackage(package!, out var files));

            var scratch = Path.Combine(work, "pkg");
            Directory.CreateDirectory(scratch);
            foreach (var pair in files!) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();

            // Write a new MaxQuantity, then reload: the writer must have found it on the component.
            Assert.True(new UAssetPackageWriter()
                .Apply(uasset, new[] { new FieldChange("MaxQuantity", "42") }).Ok);

            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var value = reloaded.Exports.OfType<NormalExport>()
                .SelectMany(x => x.Data).OfType<IntPropertyData>()
                .First(p => p.Name.ToString() == "MaxQuantity").Value;
            Assert.Equal(42, value);
        }
        finally { Directory.Delete(work, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public void A_value_nested_inside_a_struct_is_written_and_stays_written()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string well = "SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well";
        Assert.True(mount.TryReadPackage(well, out var files));

        var scratch = Path.Combine(Path.GetTempPath(), "scmw-nested-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (var pair in files!) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();

            // The well's water replenishment rate is buried two structs deep — exactly what the
            // Well3x mod changes. The writer must follow the path and leave its siblings alone.
            // The base element type is a full enum: the writer must handle that type too.
            const string path = "_replenishableResourceDescription.PeriodicReplenishment.ReplenishAmount";
            Assert.True(new UAssetPackageWriter().Apply(uasset, new[]
            {
                new FieldChange(path, "0.3"),
                new FieldChange("_baseElementType", "EBaseElementType::Well"),
            }).Ok);

            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var periodic = reloaded.Exports.OfType<NormalExport>()
                .SelectMany(e => e.Data).OfType<StructPropertyData>()
                .First(p => p.Name.ToString() == "_replenishableResourceDescription")
                .Value.OfType<StructPropertyData>()
                .First(p => p.Name.ToString() == "PeriodicReplenishment");

            Assert.Equal(0.3f,
                periodic.Value.OfType<FloatPropertyData>().First(p => p.Name.ToString() == "ReplenishAmount").Value, 3);
            Assert.Equal(40f,
                periodic.Value.OfType<FloatPropertyData>().First(p => p.Name.ToString() == "MaxAmount").Value, 3);
            Assert.Equal("EBaseElementType::Well",
                reloaded.Exports.OfType<NormalExport>().SelectMany(e => e.Data)
                    .OfType<EnumPropertyData>().First(p => p.Name.ToString() == "_baseElementType").Value.ToString());
        }
        finally { Directory.Delete(scratch, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public void A_value_inside_an_ingredient_slot_is_written_and_stays_written()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden";
        Assert.True(mount.TryReadPackage(recipe, out var files));

        var scratch = Path.Combine(Path.GetTempPath(), "scmw-ingredient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (var pair in files!) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();

            // A value buried inside a list entry — an ingredient's per-skill amount. The writer
            // must follow the list step ("Ingredients[0]") to reach it.
            const string path = "Ingredients[0].Amount.NoSkill";
            Assert.True(new UAssetPackageWriter()
                .Apply(uasset, new[] { new FieldChange(path, "7") }).Ok);

            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var ingredients = reloaded.Exports.OfType<NormalExport>().SelectMany(e => e.Data)
                .OfType<ArrayPropertyData>().First(p => p.Name.ToString() == "Ingredients");
            var slot = (StructPropertyData)ingredients.Value[0];
            var amount = (StructPropertyData)slot.Value.First(p => p.Name.ToString() == "Amount");
            var noSkill = (IntPropertyData)amount.Value.First(p => p.Name.ToString() == "NoSkill");

            Assert.Equal(7, noSkill.Value);
        }
        finally { Directory.Delete(scratch, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public void An_ingredients_accepted_tag_is_swapped_and_stays_swapped()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        // Any real crafting-ingredient tag other than the one the recipe already accepts.
        var otherTag = mount.FilePaths
            .Select(Path.GetFileNameWithoutExtension)
            .First(n => n.StartsWith("CI_", StringComparison.OrdinalIgnoreCase)
                        && !n.Equals("CI_Plank", StringComparison.OrdinalIgnoreCase));

        const string recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden";
        Assert.True(mount.TryReadPackage(recipe, out var files));

        var scratch = Path.Combine(Path.GetTempPath(), "scmw-tag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (var pair in files!) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();

            // Retarget the first ingredient's accepted tag — the risky reference write.
            const string path = "Ingredients[0].AllowedTypes[0]";
            Assert.True(new UAssetPackageWriter()
                .Apply(uasset, new[] { new FieldChange(path, otherTag) }).Ok);

            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var slot = (StructPropertyData)reloaded.Exports.OfType<NormalExport>().SelectMany(e => e.Data)
                .OfType<ArrayPropertyData>().First(p => p.Name.ToString() == "Ingredients").Value[0];
            var allowed = (ArrayPropertyData)slot.Value.First(p => p.Name.ToString() == "AllowedTypes");
            var link = (ObjectPropertyData)allowed.Value[0];

            Assert.Equal(otherTag, link.Value.ToImport(reloaded).ObjectName.ToString());
        }
        finally { Directory.Delete(scratch, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public void An_ingredient_slot_is_removed_and_another_added()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        const string recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden";
        Assert.True(mount.TryReadPackage(recipe, out var files));

        var scratch = Path.Combine(Path.GetTempPath(), "scmw-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (var pair in files!) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();

            int Count()
            {
                var asset = new UAsset(uasset, EngineVersion.VER_UE4_27);
                return asset.Exports.OfType<NormalExport>().SelectMany(e => e.Data)
                    .OfType<ArrayPropertyData>().First(p => p.Name.ToString() == "Ingredients").Value.Length;
            }

            var before = Count();
            Assert.True(before >= 2);

            // Remove one slot, then add one back — the list length must follow.
            Assert.True(new UAssetPackageWriter().Apply(uasset, new[]
            {
                new FieldChange("Ingredients[0]", "", ChangeOperation.RemoveEntry),
            }).Ok);
            Assert.Equal(before - 1, Count());

            Assert.True(new UAssetPackageWriter().Apply(uasset, new[]
            {
                new FieldChange("Ingredients", "", ChangeOperation.AddEntry),
            }).Ok);
            Assert.Equal(before, Count());
        }
        finally { Directory.Delete(scratch, true); }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real build test.")]
    public async Task An_added_field_is_written_from_a_real_class_file_donor()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        var work = Path.Combine(Path.GetTempPath(), "scmw-addreal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = new PakContentSource(new PakMount(), new AssetIndex(),
                Path.Combine(work, "a.db"), new FileIconCache(Path.Combine(work, "icons")));
            source.Configure(paks, key, null);
            Assert.Equal(ContentSourceState.Ready, (await source.ScanAsync()).State);

            // Find an item whose own file lacks the timer but whose class file (name without "_ES")
            // carries it — exactly how the app finds a donor.
            string? target = null, donor = null;
            var skip = 0;
            while (target is null && skip < 3000)
            {
                var page = await source.SearchAsync("", null, skip, 500);
                if (page.Count == 0) break;
                foreach (var a in page.Where(x => x.Id.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)))
                {
                    var classPath = a.Id[..^3];
                    var fields = await source.FieldsAsync(classPath);
                    if (fields.Any(f => f.TechnicalName == "_autoDestructTime"))
                    {
                        target = a.Id;
                        donor = classPath;
                        break;
                    }
                }
                skip += page.Count;
            }
            Assert.NotNull(target);
            Assert.NotNull(donor);

            using var mount = new PakMount();
            Assert.True(mount.TryMount(paks, key, out var error), error);
            Assert.True(mount.TryReadPackage(target!, out var targetFiles));
            Assert.True(mount.TryReadPackage(donor!, out var donorFiles));

            var targetUasset = ExtractOne(targetFiles!, Path.Combine(work, "target"));
            var donorUasset = ExtractOne(donorFiles!, Path.Combine(work, "donor"));

            // Add the timer to the item, borrowing its exact shape from the class file.
            var donors = new Dictionary<string, string> { ["_autoDestructTime"] = donorUasset };
            Assert.True(new UAssetPackageWriter().Apply(targetUasset, new[]
            {
                new FieldChange("_autoDestructTime", "0", ChangeOperation.AddField, donor),
            }, donors).Ok);

            // The file still loads and now carries the timer, as a float set to 0.
            var reloaded = new UAsset(targetUasset, EngineVersion.VER_UE4_27);
            var added = reloaded.Exports.OfType<NormalExport>().SelectMany(e => e.Data)
                .First(p => p.Name.ToString() == "_autoDestructTime");
            Assert.IsType<FloatPropertyData>(added);
            Assert.Equal(0f, ((FloatPropertyData)added).Value, 3);
        }
        finally { Directory.Delete(work, true); }
    }

    private static string ExtractOne(IReadOnlyDictionary<string, byte[]> files, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var pair in files) File.WriteAllBytes(Path.Combine(folder, pair.Key), pair.Value);
        return Directory.GetFiles(folder, "*.uasset").Single();
    }

    private static async Task<string?> FindItemWithField(IContentSource source, string fieldName)
    {
        var skip = 0;        while (skip < 5000)
        {
            var page = await source.SearchAsync("", null, skip, 500);
            if (page.Count == 0) return null;

            foreach (var asset in page.Where(a => a.Id.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)))
            {
                var fields = await source.FieldsAsync(asset.Id);
                if (fields.Any(f => f.TechnicalName == fieldName)) return asset.Id;
            }

            skip += page.Count;
        }

        return null;
    }

    private static (string Package, IReadOnlyDictionary<string, byte[]> Files) FindReadableEditableItem(
        PakMount mount, IPackageReader reader)
    {
        var tried = 0;
        foreach (var path in mount.FilePaths.Where(p =>
                     p.EndsWith("_ES.uasset", StringComparison.OrdinalIgnoreCase) &&
                     p.Contains("/Items/", StringComparison.OrdinalIgnoreCase)))
        {
            tried++;
            var package = path[..^".uasset".Length];
            if (reader.TryReadPackage(package, out var files) && files is not null)
                return (package, files);
        }

        throw new InvalidOperationException($"No readable editable item found (tried {tried}).");
    }

    private static FieldChange FirstFloatField(IReadOnlyDictionary<string, byte[]> files)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "scmw-field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (var pair in files) File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);
            var uasset = Directory.GetFiles(scratch, "*.uasset").Single();
            var asset = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var export = asset.Exports.OfType<NormalExport>()
                .First(e => e.ObjectName.ToString().StartsWith("Default__"));
            var anyFloat = export.Data.OfType<FloatPropertyData>().First();
            var newValue = (anyFloat.Value + 1f).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new FieldChange(anyFloat.Name.ToString(), newValue);
        }
        finally { Directory.Delete(scratch, true); }
    }
}
