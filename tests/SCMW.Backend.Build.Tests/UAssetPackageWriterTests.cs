using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

public class UAssetPackageWriterTests
{
    [Fact]
    public void A_missing_file_fails_plainly_and_never_throws()
    {
        var result = new UAssetPackageWriter()
            .Apply(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".uasset"),
                   new[] { new FieldChange("SomeField", "1") });

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("GridInventoryRowSpan", "GridInventoryRowSpan")]  // the game's own name
    [InlineData("GridInventoryRowSpan", "Inventory Height")]      // a legacy friendly label
    [InlineData("MaxStackSize", "Max Stack")]
    public void A_field_matches_by_technical_name_or_friendly_label(string propertyName, string field)
    {
        var names = new[] { "SomethingElse", propertyName };
        Assert.Equal(1, UAssetPackageWriter.FindPropertyIndex(names, field));
    }

    [Fact]
    public void An_unknown_field_does_not_match()
        => Assert.Equal(-1, UAssetPackageWriter.FindPropertyIndex(new[] { "A", "B" }, "Nope"));
}
