using SCMW.App;

namespace SCMW.App.Tests;

public class ScumLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-launch-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string GameRoot => _root;                                   // the Settings "game path"
    private string Scum => Path.Combine(_root, "SCUM");
    private string Exe => Path.Combine(Scum, "Binaries", "Win64", "SCUM.exe");
    private string Mods => Path.Combine(Scum, "Content", "Paks", "~mods");
    private string Disabled => Path.Combine(Scum, "Content", "~mods.disabled");

    private void MakeGame()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "");
        Directory.CreateDirectory(Mods);
        File.WriteAllText(Path.Combine(Mods, "SCMW_X_P.pak"), "pak");
    }

    private sealed class FakeProcesses : IProcessLauncher
    {
        public bool SteamRunning { get; set; } = true;
        public string? SteamExe { get; set; } = @"C:\Steam\steam.exe";
        public readonly List<(string Exe, string? Args)> Started = new();
        public bool Waited;
        public string? FindSteamExe() => SteamExe;
        public void Start(string exe, string? args) => Started.Add((exe, args));
        public Task<bool> WaitForSteamAsync(TimeSpan timeout) { Waited = true; SteamRunning = true; return Task.FromResult(true); }
    }

    [Fact]
    public async Task Modded_launches_through_steam_with_the_flags()
    {
        MakeGame();
        var proc = new FakeProcesses();
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchModdedAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal((proc.SteamExe, "-applaunch 513710 -nobattleeye -fileopenlog"), Assert.Single(proc.Started));
    }

    [Fact]
    public async Task Unmodded_launches_a_plain_game_and_touches_no_files()
    {
        MakeGame();
        var proc = new FakeProcesses();
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchUnmoddedAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal((proc.SteamExe, "-applaunch 513710"), Assert.Single(proc.Started));
        Assert.True(Directory.Exists(Mods));     // the launcher no longer moves the folder
    }

    [Fact]
    public async Task Steam_is_started_and_waited_for_when_it_is_not_running()
    {
        MakeGame();
        var proc = new FakeProcesses { SteamRunning = false };
        var launcher = new ScumLauncher(GameRoot, proc);

        await launcher.LaunchModdedAsync();

        Assert.Equal(2, proc.Started.Count);
        Assert.Equal((proc.SteamExe, null), proc.Started[0]);               // start Steam first
        Assert.Equal((proc.SteamExe, "-applaunch 513710 -nobattleeye -fileopenlog"), proc.Started[1]);
        Assert.True(proc.Waited);
    }

    [Fact]
    public async Task Without_steam_it_falls_back_to_the_game_program()
    {
        MakeGame();
        var proc = new FakeProcesses { SteamExe = null };
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchModdedAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal((Exe, "-nobattleeye -fileopenlog"), Assert.Single(proc.Started));
    }

    [Fact]
    public async Task A_missing_exe_is_not_available_and_launching_fails_plainly()
    {
        Directory.CreateDirectory(_root);
        var launcher = new ScumLauncher(GameRoot, new FakeProcesses());

        Assert.False(launcher.IsAvailable);
        var result = await launcher.LaunchModdedAsync();
        Assert.False(result.Ok);
        Assert.Contains("wasn't found", result.Error);
    }
}
