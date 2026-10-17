using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
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
            Assert.True(new UAssetCloner().TryRenameObjects(uasset, "CR_1H_Stone_Axe", "CR_SECMW_Test", out var renameError),
                renameError);

            // The copy loads and its own object is the new name — nothing still claims the donor's.
            var reloaded = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var names = reloaded.Exports.Select(e => e.ObjectName.ToString()).ToList();
            Assert.Contains("CR_SECMW_Test", names);
            Assert.DoesNotContain("CR_1H_Stone_Axe", names);
        }
        finally { Directory.Delete(dir, true); }
    }
}
