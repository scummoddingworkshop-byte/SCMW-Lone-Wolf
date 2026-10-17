using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// Laying a cloned asset out on the game's tree: a cooked package is a file named after its asset
/// plus companion files (`.uexp`, …), so every one of those is renamed to the new asset's name.
/// Getting this one level wrong (or leaving a companion behind) is how the probe's first attempt
/// failed — the game could not find the file.
/// </summary>
public class AssetClonerTests
{
    private const string Old = "CR_1H_Stone_Axe";
    private const string New = "CR_SECMW_Test";

    [Theory]
    [InlineData("CR_1H_Stone_Axe.uasset", "CR_SECMW_Test.uasset")]
    [InlineData("CR_1H_Stone_Axe.uexp", "CR_SECMW_Test.uexp")]
    [InlineData("CR_1H_Stone_Axe.ubulk", "CR_SECMW_Test.ubulk")]
    public void The_asset_file_and_its_companions_take_the_new_name(string file, string expected)
        => Assert.Equal(expected, CloneLayout.StagedFileName(file, Old, New));

    [Theory]
    [InlineData("CR_1H_Stone_Axe_Extra.uasset")]   // a different asset whose name merely starts the same
    [InlineData("SomethingElse.uasset")]
    public void Unrelated_files_keep_their_name(string file)
        => Assert.Equal(file, CloneLayout.StagedFileName(file, Old, New));
}
