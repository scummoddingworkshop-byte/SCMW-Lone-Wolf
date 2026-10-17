using SCMW.Backend.Build;
using SCMW.Testing;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The strongest proof of the registry writer: point it at SCUM's own <c>AssetRegistry.bin</c>
/// (the whole 49 MB index), add one asset, and read the result straight back. Skips when the file
/// is not available, so it never blocks a normal test run.
/// </summary>
public class AssetRegistryRealTests
{
    [SkippableFact(EnvVars = "SCMW_REGISTRY_PATH",
                   EnvSkipReason = "Set SCMW_REGISTRY_PATH to a real SCUM AssetRegistry.bin to run this test.")]
    public void Adding_an_asset_to_the_real_index_rereads_clean()
    {
        var path = Environment.GetEnvironmentVariable("SCMW_REGISTRY_PATH")!;
        var original = File.ReadAllBytes(path);

        Assert.True(AssetRegistryReader.TryRead(original, out var before, out var readError), readError);
        Assert.Equal(8, before!.Version);
        Assert.Equal(0, before.HashMismatches);

        var donor = before.Assets.First(a => a.Tags.Any(t => t.Key == "PrimaryAssetName"));
        var packagePath = donor.PackagePath + "/SCMWTest";

        Assert.True(AssetRegistryWriter.TryAddAsset(original,
            new NewRegistryAsset(packagePath, "CR_SECMW_Test", donor.AssetName),
            out var result, out var error), error);

        Assert.True(AssetRegistryReader.TryRead(result!, out var after, out var again), again);
        Assert.Equal(before.Assets.Count + 1, after!.Assets.Count);
        Assert.Equal(0, after.HashMismatches);           // our new names carry correct hashes

        var added = after.Assets.Single(a => a.AssetName == "CR_SECMW_Test");
        Assert.Contains(added.Tags, t => t.Key == "PrimaryAssetName" && t.Value == "CR_SECMW_Test");
        Assert.Equal(donor.AssetClass, added.AssetClass);
    }
}
