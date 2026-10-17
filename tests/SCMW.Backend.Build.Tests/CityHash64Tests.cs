using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// The name hash the game stores in its asset index: CityHash64 of the lower-cased name. The
/// expected values below are real ones read straight out of SCUM's own AssetRegistry.bin, so they
/// pin this port to the engine's own output — not to our own arithmetic.
/// </summary>
public class CityHash64Tests
{
    [Theory]
    [InlineData("/Game", 0x8117EF25CCC98E9AUL)]
    [InlineData("InitBank", 0xFC03D4F0A4B19269UL)]
    [InlineData("Blueprint", 0x47F551F712E884FFUL)]
    [InlineData("AkInitBank", 0xBF94D7D4AE3DD8B9UL)]
    public void HashName_matches_the_games_own_stored_hashes(string name, ulong expected)
        => Assert.Equal(expected, CityHash64.HashName(name));

    [Fact]
    public void HashName_ignores_letter_case()
        => Assert.Equal(CityHash64.HashName("initbank"), CityHash64.HashName("INITBANK"));

    [Fact]
    public void Hash_of_nothing_is_the_cityhash64_empty_vector()
        => Assert.Equal(0x9ae16a3b2f90404fUL, CityHash64.Hash(ReadOnlySpan<byte>.Empty));
}
