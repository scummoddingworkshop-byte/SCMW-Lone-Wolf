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
}
