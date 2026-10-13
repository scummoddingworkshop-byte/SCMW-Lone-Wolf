using SCMW.Backend.Build;
using SCMW.Testing;

namespace SCMW.Backend.Build.Tests;

public class RepakPackerTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public string? Exe; public IReadOnlyList<string>? Args; public ProcessResult Next = new(0, "");
        public string? CreateFile;
        public Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, CancellationToken ct)
        {
            Exe = exePath; Args = args;
            if (CreateFile is not null) File.WriteAllText(CreateFile, "pak");
            return Task.FromResult(Next);
        }
    }

    [Fact]
    public async Task Packer_runs_repak_pack_with_the_staging_folder_and_the_pak_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-stage-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "a.uasset"), "x");
        var exe = Path.Combine(root, "repak.exe"); File.WriteAllText(exe, "");
        var pak = Path.Combine(root, "out.pak");
        var runner = new FakeRunner { CreateFile = pak };
        var packer = new RepakPacker(exe, runner);

        var result = await packer.PackAsync(staging, pak, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "pack", staging, pak }, runner.Args);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task A_missing_repak_fails_plainly_and_never_lies()
    {
        var packer = new RepakPacker(@"C:\nope\repak.exe", new FakeRunner());
        var result = await packer.PackAsync(Path.GetTempPath(), "x.pak", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Contains("packing tool", result.Error);
    }

    [Fact]
    public async Task A_repak_error_fails_the_pack()
    {
        var staging = Path.Combine(Path.GetTempPath(), "scmw-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "a.uasset"), "x");
        var runner = new FakeRunner { Next = new(1, "boom") };
        var exe = Path.Combine(staging, "repak.exe"); File.WriteAllText(exe, "");
        var result = await new RepakPacker(exe, runner)
            .PackAsync(staging, Path.Combine(staging, "o.pak"), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Contains("couldn't make the mod file", result.Error);
        Assert.DoesNotContain("boom", result.Error);   // the tool's raw output stays out of the message
        Directory.Delete(staging, true);
    }

    [SkippableFact(SkipIfMissingFile = "tools/repak/repak.exe")]
    public async Task The_bundled_repak_really_packs_a_folder()
    {
        var repak = Path.Combine(AppContext.BaseDirectory, "tools", "repak", "repak.exe");

        var root = Path.Combine(Path.GetTempPath(), "scmw-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "SCUM", "Content"));
        File.WriteAllText(Path.Combine(root, "SCUM", "Content", "thing.uasset"), "hello");
        var pak = Path.Combine(root, "mod.pak");
        try
        {
            var runner = new SystemProcessRunner();
            var result = await new RepakPacker(repak, runner).PackAsync(root, pak, CancellationToken.None);
            Assert.True(result.Ok, result.Error);
            Assert.True(File.Exists(pak));

            var listed = await runner.RunAsync(repak, new[] { "list", pak }, CancellationToken.None);
            Assert.Contains("SCUM/Content/thing.uasset", listed.Output.Replace('\\', '/'));
        }
        finally { Directory.Delete(root, true); }
    }
}
