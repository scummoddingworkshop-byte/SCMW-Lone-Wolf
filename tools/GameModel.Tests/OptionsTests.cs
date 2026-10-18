namespace SCMW.GameModel;

/// <summary>Parses the tool's command line.</summary>
public class OptionsTests
{
    [Fact]
    public void Reads_the_required_output_folder()
    {
        Assert.True(Options.TryParse(new[] { "--out", @"C:\somewhere" }, out var options, out _));
        Assert.Equal(@"C:\somewhere", options.OutDir);
        Assert.Null(options.GamePath);
        Assert.Null(options.Limit);
    }

    [Fact]
    public void Fails_plainly_when_the_output_folder_is_missing()
    {
        Assert.False(Options.TryParse(Array.Empty<string>(), out _, out var error));
        Assert.Contains("--out", error);
    }

    [Fact]
    public void Reads_optional_game_path_key_and_limit()
    {
        Assert.True(Options.TryParse(
            new[] { "--out", "O", "--game", "G", "--key", "K", "--limit", "5" },
            out var options, out _));

        Assert.Equal("G", options.GamePath);
        Assert.Equal("K", options.AesKey);
        Assert.Equal(5, options.Limit);
    }

    [Fact]
    public void Rejects_a_limit_that_is_not_a_number()
    {
        Assert.False(Options.TryParse(new[] { "--out", "O", "--limit", "lots" }, out _, out var error));
        Assert.Contains("--limit", error);
    }

    [Fact]
    public void Rejects_an_unknown_option()
    {
        Assert.False(Options.TryParse(new[] { "--out", "O", "--bogus" }, out _, out var error));
        Assert.Contains("--bogus", error);
    }

    [Fact]
    public void Reads_a_dump_word_without_an_output_folder()
    {
        Assert.True(Options.TryParse(new[] { "--dump", "Water_Bottle" }, out var options, out _));
        Assert.Equal("Water_Bottle", options.Dump);
        Assert.Equal(string.Empty, options.OutDir);
        Assert.Null(options.Field);
    }

    [Fact]
    public void Reads_a_dump_field_filter()
    {
        Assert.True(Options.TryParse(new[] { "--dump", "Water", "--field", "Weight" }, out var options, out _));
        Assert.Equal("Water", options.Dump);
        Assert.Equal("Weight", options.Field);
    }

    [Fact]
    public void Fails_when_neither_out_nor_dump_is_given()
    {
        Assert.False(Options.TryParse(new[] { "--game", "G" }, out _, out var error));
        Assert.Contains("--out", error);
        Assert.Contains("--dump", error);
    }

    [Fact]
    public void Rejects_field_without_dump()
    {
        Assert.False(Options.TryParse(new[] { "--out", "O", "--field", "Weight" }, out _, out var error));
        Assert.Contains("--field", error);
    }

    [Fact]
    public void Reads_a_make_target_and_its_sets()
    {
        Assert.True(Options.TryParse(
            new[] { "--make", "out.pak", "--set", "SCUM/Content/Items/Thing_ES", "Caption=Hello World",
                    "--set", "SCUM/Content/Items/Thing_ES", "MaxQuantity=5" },
            out var options, out _));

        Assert.Equal("out.pak", options.Make);
        Assert.Equal(2, options.Sets.Count);
        Assert.Equal("SCUM/Content/Items/Thing_ES", options.Sets[0].AssetId);
        Assert.Equal("Caption", options.Sets[0].Field);
        Assert.Equal("Hello World", options.Sets[0].Value);
        Assert.Equal("MaxQuantity", options.Sets[1].Field);
    }

    [Fact]
    public void A_set_value_keeps_an_equals_sign_in_the_value()
    {
        Assert.True(Options.TryParse(
            new[] { "--make", "o.pak", "--set", "A", "Note=a=b" }, out var options, out _));
        Assert.Equal("a=b", options.Sets[0].Value);
    }

    [Fact]
    public void Rejects_a_set_without_a_make()
    {
        Assert.False(Options.TryParse(new[] { "--dump", "X", "--set", "A", "F=1" }, out _, out var error));
        Assert.Contains("--set", error);
    }

    [Fact]
    public void Rejects_a_make_without_any_set()
    {
        Assert.False(Options.TryParse(new[] { "--make", "o.pak" }, out _, out var error));
        Assert.Contains("--set", error);
    }

    [Fact]
    public void Rejects_more_than_one_mode()
    {
        Assert.False(Options.TryParse(new[] { "--out", "O", "--dump", "X" }, out _, out var error));
        Assert.Contains("only one", error);
    }
}
