using CUE4Parse.UE4.Versions;
using Xunit;

public class EngineMappingTests
{
    [Fact]
    public void Scum_engine_mapping_is_UE4_27()
    {
        var vc = new VersionContainer(EGame.GAME_UE4_27);
        Assert.Equal(EGame.GAME_UE4_27, vc.Game);
    }
}
