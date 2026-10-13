using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

public class BuildNamesTests
{
    [Theory]
    [InlineData("My first mod", "My-first-mod")]
    [InlineData("  Tweak / Tune  ", "Tweak-Tune")]
    [InlineData("", "mod")]
    [InlineData("///", "mod")]
    public void Safe_name_is_a_windows_safe_folder_name(string input, string expected)
        => Assert.Equal(expected, BuildNames.SafeName(input));

    [Fact]
    public void Pak_name_is_our_own_patch_pak()
        => Assert.Equal("SCMW_My-first-mod_v1.0.0_P.pak", BuildNames.PakFileName("My first mod", "1.0.0"));

    [Fact]
    public void A_blank_or_junk_version_falls_back_to_1_0_0()
    {
        Assert.Equal("SCMW_Mod_v1.0.0_P.pak", BuildNames.PakFileName("Mod", ""));
        Assert.Equal("SCMW_Mod_v1.0.0_P.pak", BuildNames.PakFileName("Mod", "latest"));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("v2.10.3", true)]
    [InlineData("", false)]
    [InlineData("latest", false)]
    [InlineData("1.0", false)]
    [InlineData("1.0.0.0", false)]
    public void A_version_is_checked_before_building(string version, bool expected)
        => Assert.Equal(expected, BuildNames.IsValidVersion(version));

    [Fact]
    public void Old_version_matching_is_exact_and_never_a_lookalike()
    {
        Assert.True(BuildNames.IsOlderPakOfProject("SCMW_Tape_v1.0.0_P.pak", "Tape"));
        Assert.True(BuildNames.IsOlderPakOfProject("SCMW_Tape_v2.10.3_P.pak", "Tape"));
        // A project whose name is a prefix must never match the longer one's file:
        Assert.False(BuildNames.IsOlderPakOfProject("SCMW_Tape-Extra_v1.0.0_P.pak", "Tape"));
        // A different tool's pak must never match:
        Assert.False(BuildNames.IsOlderPakOfProject("FD_SCUM_Tape_v1.0.0_P.pak", "Tape"));
    }

    [Fact]
    public void The_mods_folder_sits_under_the_games_paks_folder()
        => Assert.EndsWith(Path.Combine("SCUM", "Content", "Paks", "~mods"),
                           BuildNames.ModsFolder(@"C:\Games\SCUM"));
}
