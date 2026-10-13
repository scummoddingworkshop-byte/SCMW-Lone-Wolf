using SCMW.Backend.Pak;
using SCMW.Testing;
using Xunit;

namespace SCMW.Backend.Pak.Tests;

/// <summary>
/// The library half of the nested reader: parse a package's raw bytes with UAssetAPI and walk
/// inside its structs. The parse path needs a real package, so the full test is env-gated
/// (SCMW_SCUM_PATH + SCMW_TEST_AES_KEY) like the other real-install tests.
/// </summary>
public class NestedValueReaderTests
{
    private const string EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY";
    private const string SkipReason =
        "SCMW_SCUM_PATH and SCMW_TEST_AES_KEY must be set to run the real-install test.";

    [Fact]
    public void No_files_yields_no_nested_values()
    {
        Assert.Empty(NestedValueReader.Read(new Dictionary<string, byte[]>()));
    }

    [SkippableFact(EnvVars = EnvVars, EnvSkipReason = SkipReason)]
    public void The_wells_water_numbers_are_read_from_inside_its_struct()
    {
        var pakPath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;

        using var mount = new PakMount();
        Assert.True(mount.TryMount(pakPath, key, out var error), error);

        const string well = "SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well";
        Assert.True(mount.TryReadPackage(well, out var files));

        var nested = NestedValueReader.Read(files!);

        const string path = "_replenishableResourceDescription.PeriodicReplenishment.ReplenishAmount";
        var amount = Assert.Single(nested, f => f.TechnicalName == path);
        Assert.Equal("0.1", amount.CurrentValue);
        Assert.Equal("FloatProperty", amount.ValueTypeHint);
        Assert.True(amount.Nested);
    }

    [SkippableFact(EnvVars = EnvVars, EnvSkipReason = SkipReason)]
    public async Task A_recipe_reads_with_its_ingredient_slots_and_accepted_tags()
    {
        var pakPath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;

        using var mount = new PakMount();
        Assert.True(mount.TryMount(pakPath, key, out var error), error);

        const string recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden";
        Assert.True(mount.TryReadPackage(recipe, out var files));
        var nested = NestedValueReader.Read(files!);

        // A recipe is a data asset (no default object): its plain values, its per-skill rewards
        // and its ingredient slots all come from the writer's library.
        Assert.Contains(nested, f => f.TechnicalName == "Caption");
        Assert.Contains(nested, f => f.TechnicalName == "ExperienceReward.NoSkill");
        Assert.Contains(nested, f => f.TechnicalName == "Ingredients[0].Amount.NoSkill");
        Assert.Contains(nested, f => f.TechnicalName == "Ingredients[0].AllowedTypes[0]" && f.CurrentValue == "CI_Plank");

        // The source reads it end-to-end through the no-default-object path.
        var source = new PakContentSource(mount);
        source.Configure(pakPath, key, null);
        var fields = await source.FieldsAsync(recipe);
        Assert.Contains(fields, f => f.TechnicalName == "Ingredients[0].Amount.NoSkill");
    }
}
