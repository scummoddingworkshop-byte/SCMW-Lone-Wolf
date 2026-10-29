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
        public bool ThrowOnStart { get; set; }
        public readonly List<(string Exe, string? Args)> Started = new();
        public bool Waited;
        public string? FindSteamExe() => SteamExe;
        public void Start(string exe, string? args)
        {
            if (ThrowOnStart) throw new InvalidOperationException("start refused");
            Started.Add((exe, args));
        }
        public Task<bool> WaitForSteamAsync(TimeSpan timeout) { Waited = true; SteamRunning = true; return Task.FromResult(true); }
    }

    [Fact]
    public async Task Modded_launches_the_game_program_directly_with_the_flags()
    {
        MakeGame();
        var proc = new FakeProcesses();
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchModdedAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal((Exe, "-nobattleye -fileopenlog"), Assert.Single(proc.Started));
    }

    [Fact]
    public async Task Unmodded_launches_the_game_program_directly_with_no_flags()
    {
        MakeGame();
        var proc = new FakeProcesses();
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchUnmoddedAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal((Exe, (string?)null), Assert.Single(proc.Started));
    }

    [Fact]
    public async Task Steam_already_open_is_still_waited_for()
    {
        MakeGame();
        var proc = new FakeProcesses { SteamRunning = true };
        var launcher = new ScumLauncher(GameRoot, proc);

        await launcher.LaunchModdedAsync();

        Assert.True(proc.Waited);                                  // open is not the same as signed in
        Assert.Equal((Exe, "-nobattleye -fileopenlog"), Assert.Single(proc.Started));
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
        Assert.Equal((Exe, "-nobattleye -fileopenlog"), proc.Started[1]);
        Assert.True(proc.Waited);
    }

    [Fact]
    public async Task Without_steam_it_still_starts_the_game_program()
    {
        MakeGame();
        var proc = new FakeProcesses { SteamExe = null };
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchModdedAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal((Exe, "-nobattleye -fileopenlog"), Assert.Single(proc.Started));
    }

    [Fact]
    public async Task A_failed_start_reports_a_plain_error()
    {
        MakeGame();
        var proc = new FakeProcesses { ThrowOnStart = true };
        var launcher = new ScumLauncher(GameRoot, proc);

        var result = await launcher.LaunchModdedAsync();

        Assert.False(result.Ok);
        Assert.Equal("SCUM could not be started.", result.Error);
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
