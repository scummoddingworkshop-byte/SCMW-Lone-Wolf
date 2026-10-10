using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
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

    private static async Task<string?> FindItemWithField(IContentSource source, string fieldName)
    {
        var skip = 0;
        while (skip < 5000)
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
