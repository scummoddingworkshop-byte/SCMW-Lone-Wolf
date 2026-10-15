namespace SCMW.GameModel;

/// <summary>Guards and small path helpers the reader needs.</summary>
public class OutputGuardTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gm-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Allows_a_folder_that_does_not_exist_yet()
        => Assert.Null(OutputGuard.Check(Path.Combine(Path.GetTempPath(), "gm-missing-" + Guid.NewGuid().ToString("N")), limited: false));

    [Fact]
    public void Allows_an_empty_folder()
        => Assert.Null(OutputGuard.Check(NewTempDir(), limited: false));

    [Theory]
    [InlineData("README.md")]
    [InlineData("index.md")]
    [InlineData("meanings.md")]
    public void Allows_a_folder_that_looks_like_the_notebook(string marker)
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, marker), "x");

        Assert.Null(OutputGuard.Check(dir, limited: false));
    }

    [Fact]
    public void Refuses_a_folder_holding_unrelated_files()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "holiday-photos.txt"), "x");

        var error = OutputGuard.Check(dir, limited: false);
        Assert.NotNull(error);
        Assert.Contains("notebook", error);
    }

    [Fact]
    public void Refuses_a_limited_run_over_a_full_notebook()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "index.md"), "The Game Model — a full notebook");

        var error = OutputGuard.Check(dir, limited: true);
        Assert.NotNull(error);
        Assert.Contains("limit", error);
    }

    [Fact]
    public void Allows_a_limited_run_over_a_limited_notebook()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "index.md"), "> **Limited run:** only the first 5 assets");

        Assert.Null(OutputGuard.Check(dir, limited: true));
    }

    [Theory]
    [InlineData(@"C:\X\SCUM", "C:/X/SCUM/", true)]
    [InlineData(@"C:\X\SCUM", @"c:\x\scum", true)]
    [InlineData(@"C:\X\SCUM", @"C:\Y\SCUM", false)]
    [InlineData(null, "C:/X", false)]
    public void Same_path_compares_separators_and_case(string? a, string? b, bool expected)
        => Assert.Equal(expected, OutputGuard.SamePath(a, b));
}
