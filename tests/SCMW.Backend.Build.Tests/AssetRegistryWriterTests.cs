using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The registry writer: adds one brand-new asset to the game's own asset index (and the copy of it
/// a content-adding mod must ship). Proved in game by the probe — these tests pin its behaviour.
/// </summary>
public class AssetRegistryWriterTests
{
    private const string PackagePath = "/Game/ConZ_Files/Items/Crafting/Recipes/Items";
    private const string DonorName = "CR_1H_Stone_Axe";
    private const string NewName = "CR_SECMW_Test";
    private const string NewFolder = PackagePath + "/SCMWTest";

    private static FakeAssetRegistry WithDonor()
    {
        var reg = new FakeAssetRegistry();
        const string cls = "ItemCraftingRecipe";

        reg.Name("PrimaryAssetType");
        reg.Name("PrimaryAssetName");
        reg.Name("NativeClass");
        reg.NumberlessNames.Add((uint)reg.Name(cls));

        // Three numberless pairs: type, name, class. The writer must rewrite only "name".
        reg.Pairs.Add(((uint)reg.Name("PrimaryAssetType"), 2u));
        reg.Pairs.Add(((uint)reg.Name("PrimaryAssetName"), 2u));
        reg.Pairs.Add(((uint)reg.Name("NativeClass"), 2u));

        reg.AddRecord(new FakeRecord(new[]
        {
            $"{PackagePath}/{DonorName}.{DonorName}",   // old object path
            PackagePath,                                 // package path
            cls,                                         // asset class
            $"{PackagePath}/{DonorName}",                // package name
            DonorName,                                   // asset name
        }, TagHandle: 0x8000000300000000UL, ChunkIds: new[] { 0 }));

        return reg;
    }

    private static bool TryAdd(byte[] registry, string newName, string folder, string donor,
        out byte[]? result, out string? error)
        => AssetRegistryWriter.TryAddAsset(registry,
            new NewRegistryAsset(folder, newName, donor), out result, out error);

    [Fact]
    public void Adds_a_new_asset_with_its_own_name_and_folder()
    {
        var original = WithDonor().Build();
        Assert.True(AssetRegistryReader.TryRead(original, out var before, out var readError), readError);

        Assert.True(TryAdd(original, NewName, NewFolder, DonorName, out var result, out var error), error);

        Assert.True(AssetRegistryReader.TryRead(result!, out var after, out var again), again);
        Assert.Equal(before!.Assets.Count + 1, after!.Assets.Count);

        var added = after.Assets.Single(a => a.AssetName == NewName);
        Assert.Equal(NewFolder, added.PackagePath);
        Assert.Equal($"{NewFolder}/{NewName}", added.PackageName);
        Assert.Equal($"{NewFolder}/{NewName}.{NewName}", added.OldObjectPath);
        Assert.Equal("ItemCraftingRecipe", added.AssetClass);
    }

    [Fact]
    public void The_new_asset_carries_its_own_primary_asset_name_not_the_donors()
    {
        var original = WithDonor().Build();
        Assert.True(TryAdd(original, NewName, NewFolder, DonorName, out var result, out var error), error);
        Assert.True(AssetRegistryReader.TryRead(result!, out var after, out var readError), readError);

        var added = after!.Assets.Single(a => a.AssetName == NewName);
        Assert.Contains(added.Tags, t => t.Key == "PrimaryAssetName" && t.Value == NewName);
        Assert.Contains(added.Tags, t => t.Key == "PrimaryAssetType" && t.Value == "ItemCraftingRecipe");
        Assert.Contains(added.Tags, t => t.Key == "NativeClass");
    }

    [Fact]
    public void The_donor_and_every_other_asset_are_left_in_place()
    {
        var original = WithDonor().Build();
        Assert.True(TryAdd(original, NewName, NewFolder, DonorName, out var result, out var error), error);
        Assert.True(AssetRegistryReader.TryRead(result!, out var after, out var readError), readError);

        Assert.Contains(after!.Assets, a => a.AssetName == DonorName);
        Assert.Equal(0, after.HashMismatches);
    }

    [Fact]
    public void A_registry_that_is_not_version_8_is_refused()
    {
        var reg = WithDonor();
        reg.Version = 7;

        Assert.False(TryAdd(reg.Build(), NewName, NewFolder, DonorName, out var result, out var error));
        Assert.Null(result);
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_registry_without_the_donor_is_refused()
    {
        var original = WithDonor().Build();

        Assert.False(TryAdd(original, NewName, NewFolder, "CR_No_Such_Recipe", out var result, out var error));
        Assert.Null(result);
        Assert.Contains("donor", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_new_assets_can_be_added_one_after_another()
    {
        var registry = WithDonor().Build();
        Assert.True(TryAdd(registry, "CR_SECMW_One", NewFolder, DonorName, out var once, out var e1), e1);
        Assert.True(TryAdd(once!, "CR_SECMW_Two", NewFolder, DonorName, out var twice, out var e2), e2);

        Assert.True(AssetRegistryReader.TryRead(twice!, out var state, out var e3), e3);
        Assert.Contains(state!.Assets, a => a.AssetName == "CR_SECMW_One");
        Assert.Contains(state.Assets, a => a.AssetName == "CR_SECMW_Two");
        Assert.Equal(0, state.HashMismatches);
    }
}
