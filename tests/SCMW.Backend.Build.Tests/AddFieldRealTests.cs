using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Testing;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The real pairing behind "add a field to the item's class file": an item's "_ES" setup names its
/// item in its own <c>Actor</c> link, and the item's name need not be the "_ES" name minus "_ES".
/// This was the cause of a build failure on AnimalBaitFeeder (2026-10-23).
/// </summary>
public class AddFieldRealTests
{
    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real pairing test.")]
    public void A_real_ES_names_an_item_whose_name_differs()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;
        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");

        using var mount = new PakMount();
        Assert.True(mount.TryMount(paks, key, out var error), error);

        // The name rule would target ".../Equipment/AnimalBaitFeeder" — which the game does not have.
        Assert.False(mount.TryReadPackage("SCUM/Content/ConZ_Files/Items/Equipment/AnimalBaitFeeder", out _));

        var source = new PakContentSource(mount);
        source.Configure(paks, key, null);
        var fields = source.FieldsAsync("SCUM/Content/ConZ_Files/Items/Equipment/AnimalBaitFeeder_ES")
            .GetAwaiter().GetResult();

        var actor = fields.FirstOrDefault(f => string.Equals(f.TechnicalName, "Actor", StringComparison.OrdinalIgnoreCase))
            ?.CurrentValue;
        Assert.False(string.IsNullOrWhiteSpace(actor));

        var itemPackage = ItemSoftPaths.PackageOf(actor);
        Assert.Equal("SCUM/Content/ConZ_Files/Items/Equipment/AnimalBaitFeederItem", itemPackage);
        Assert.True(mount.TryReadPackage(itemPackage, out _));   // the file the field really goes on
    }
}
