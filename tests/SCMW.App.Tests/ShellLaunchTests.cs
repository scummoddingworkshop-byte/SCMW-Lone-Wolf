using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ShellLaunchTests
{
    private sealed class FakeLauncher : IGameLauncher
    {
        public bool IsAvailable { get; set; } = true;
        public int Modded; public int Unmodded;
        public LaunchResult Next = new(true, null);
        public Task<LaunchResult> LaunchModdedAsync() { Modded++; return Task.FromResult(Next); }
        public Task<LaunchResult> LaunchUnmoddedAsync() { Unmodded++; return Task.FromResult(Next); }
    }

    [AvaloniaFact]
    public async Task Launch_modded_starts_the_modded_game()
    {
        var launcher = new FakeLauncher();
        var vm = new ShellViewModel(new NavigationHost(), launcher: launcher, saves: new FakeSaves());

        await vm.LaunchModdedCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.Modded);
        Assert.Equal(0, launcher.Unmodded);
    }

    [AvaloniaFact]
    public async Task A_modded_launch_backs_up_the_save_first()
    {
        var saves = new FakeSaves();
        var vm = new ShellViewModel(new NavigationHost(), launcher: new FakeLauncher(), saves: saves);

        await vm.LaunchModdedCommand.ExecuteAsync(null);

        Assert.Equal(1, saves.Backups);
    }

    [AvaloniaFact]
    public async Task A_vanilla_launch_does_not_back_up_the_save()
    {
        var saves = new FakeSaves();
        var vm = new ShellViewModel(new NavigationHost(), launcher: new FakeLauncher(), saves: saves);

        await vm.LaunchUnmoddedCommand.ExecuteAsync(null);

        Assert.Equal(0, saves.Backups);
    }

    [AvaloniaFact]
    public async Task Launch_unmodded_starts_the_vanilla_game_and_says_the_mods_are_off()
    {
        var launcher = new FakeLauncher();
        var vm = new ShellViewModel(new NavigationHost(), launcher: launcher);

        await vm.LaunchUnmoddedCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.Unmodded);
        Assert.Contains("off", vm.LaunchNote, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.ShowLaunchNote);
    }

    [AvaloniaFact]
    public void With_the_program_found_the_launch_buttons_are_ready()
    {
        var vm = new ShellViewModel(new NavigationHost(), launcher: new FakeLauncher { IsAvailable = true });

        Assert.True(vm.CanLaunch);
        Assert.True(vm.LaunchModdedCommand.CanExecute(null));
        Assert.True(vm.LaunchUnmoddedCommand.CanExecute(null));
        Assert.False(vm.ShowLaunchNote);
    }

    [AvaloniaFact]
    public async Task A_failed_launch_shows_the_plain_reason()
    {
        var launcher = new FakeLauncher { Next = new LaunchResult(false, "SCUM could not be started.") };
        var vm = new ShellViewModel(new NavigationHost(), launcher: launcher, saves: new FakeSaves());

        await vm.LaunchModdedCommand.ExecuteAsync(null);

        Assert.Equal("SCUM could not be started.", vm.LaunchNote);
        Assert.True(vm.ShowLaunchNote);
    }

    private sealed class FakeMods : IModsService
    {
        public bool IsAvailable => true;
        public readonly List<bool> AllSwitches = new();
        public ModsResult SetAllResult = new(true, null);
        public IReadOnlyList<ModsEntry> List() => Array.Empty<ModsEntry>();
        public ModsResult SetEnabled(string file, bool enabled) => new(true, null);
        public ModsResult SetAllEnabled(bool enabled) { AllSwitches.Add(enabled); return SetAllResult; }
        public ModsResult Remove(string file) => new(true, null);
        public ImportOutcome Import(IReadOnlyList<string> paths) => new(true, null, 0, 0);
        public PackOutcome PackOurMods(string zipPath) => new(false, "no mods here", 0, Array.Empty<string>());
    }

    [AvaloniaFact]
    public async Task Launch_unmodded_switches_all_mods_off_first_and_modded_switches_them_on()
    {
        var launcher = new FakeLauncher();
        var mods = new FakeMods();
        var vm = new ShellViewModel(new NavigationHost(), launcher: launcher, modsService: mods, saves: new FakeSaves());

        await vm.LaunchUnmoddedCommand.ExecuteAsync(null);
        await vm.LaunchModdedCommand.ExecuteAsync(null);

        Assert.Equal(new[] { false, true }, mods.AllSwitches);
    }

    [AvaloniaFact]
    public async Task When_the_mods_cannot_be_switched_the_game_is_not_started()
    {
        var launcher = new FakeLauncher();
        var mods = new FakeMods { SetAllResult = new ModsResult(false, "Close the game, then try again.") };
        var vm = new ShellViewModel(new NavigationHost(), launcher: launcher, modsService: mods);

        await vm.LaunchUnmoddedCommand.ExecuteAsync(null);

        Assert.Equal(0, launcher.Modded + launcher.Unmodded);   // never started
        Assert.Contains("Close the game", vm.LaunchNote);
    }

    private sealed class FakeSaves : ISaveBackupsService
    {
        public int Backups;
        public bool IsAvailable => true;
        public string SaveFolder => "";
        public IReadOnlyList<SaveSnapshot> List() => Array.Empty<SaveSnapshot>();
        public string? BackUp() { Backups++; return null; }
        public string? Restore(string id) => null;
        public string? Delete(string id) => null;
    }
}
