using Avalonia.Headless.XUnit;
using SCMW.Backend.Build;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

public class BuildRealServiceTests : IDisposable
{
    private readonly List<string> _roots = new();
    public void Dispose() { foreach (var r in _roots) TestProjects.Cleanup(r); }

    private sealed class FakeService : IBuildService
    {
        public BuildOutcome BuildResult = new(true, @"C:\ws\Builds\M\SCMW_M_v1.0.0_P.pak", null);
        public InstallOutcome InstallResult = new(true, @"C:\g\~mods\SCMW_M_v1.0.0_P.pak", null);
        public RemoveOutcome RemoveResult = new(true, null);
        public string? RemovedProjectId;
        public InstallResult_ Arg;

        public Task<BuildOutcome> BuildAsync(string name, string version, IReadOnlyList<BuildItem> items,
            IProgress<int>? progress, CancellationToken ct)
        {
            SentItems = items;
            progress?.Report(0); progress?.Report(4);
            return Task.FromResult(BuildResult);
        }

        public IReadOnlyList<BuildItem>? SentItems;

        public Task<InstallOutcome> InstallAsync(string id, string name, string version, string pak, CancellationToken ct)
        { Arg = new(id, name, version, pak); return Task.FromResult(InstallResult); }

        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct)
        { RemovedProjectId = id; return Task.FromResult(RemoveResult); }

        public bool Installed;
        public bool IsInstalled(string id) => Installed;

        public readonly record struct InstallResult_(string Id, string Name, string Version, string Pak);
    }

    private sealed class ControllableService : IBuildService
    {
        private readonly TaskCompletionSource<BuildOutcome> _tcs = new();
        public void Complete(BuildOutcome outcome) => _tcs.TrySetResult(outcome);
        public Task<BuildOutcome> BuildAsync(string n, string v, IReadOnlyList<BuildItem> i, IProgress<int>? p, CancellationToken ct)
            => _tcs.Task;
        public Task<InstallOutcome> InstallAsync(string id, string n, string v, string pak, CancellationToken ct)
            => Task.FromResult(new InstallOutcome(true, "path", null));
        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct) => Task.FromResult(new RemoveOutcome(true, null));
        public bool IsInstalled(string id) => false;
    }

    private (BuildViewModel vm, ProjectStore projects, FakeService service) New()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new FakeService();
        return (new BuildViewModel(changes, projects: projects, service: service), projects, service);
    }

    [AvaloniaFact]
    public async Task Build_runs_the_real_service_and_remembers_the_pak()
    {
        var (vm, _, _) = New();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsSucceeded);
        Assert.Equal(@"C:\ws\Builds\M\SCMW_M_v1.0.0_P.pak", vm.BuiltPakPath);
        Assert.True(vm.CanInstall);
    }

    [AvaloniaFact]
    public async Task Install_sends_the_built_pak_to_scum()
    {
        var (vm, projects, service) = New();
        await vm.BuildCommand.ExecuteAsync(null);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(projects.Current!.Id, service.Arg.Id);
        Assert.Equal(vm.BuiltPakPath, service.Arg.Pak);
        Assert.True(vm.HasInstalled);
    }

    [AvaloniaFact]
    public async Task After_installing_there_is_nothing_left_to_install()
    {
        var (vm, _, _) = New();
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.True(vm.ShowInstallButton);
        Assert.Equal("Install into SCUM", vm.InstallButtonLabel);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.True(vm.HasInstalled);
        Assert.False(vm.ShowInstallButton);        // the copy in SCUM is exactly this build
        Assert.Equal("Your mod is ready and installed into SCUM.", vm.SuccessMessage);
    }

    [AvaloniaFact]
    public async Task Changing_after_installing_offers_an_update_not_a_second_install()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new FakeService { Installed = true };
        var vm = new BuildViewModel(changes, projects: projects, service: service);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.HasInstalled);              // SCUM still has the earlier copy
        Assert.True(vm.ShowInstallButton);
        Assert.Equal("Update the copy in SCUM", vm.InstallButtonLabel);
        Assert.Contains("replace", vm.InstallHint);
        Assert.Contains("earlier copy", vm.SuccessMessage);

        await vm.InstallCommand.ExecuteAsync(null);
        Assert.False(vm.ShowInstallButton);        // updated: up to date again
    }

    [AvaloniaFact]
    public async Task Removing_clears_the_installed_state()
    {
        var (vm, _, _) = New();
        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.True(vm.HasInstalled);

        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.False(vm.HasInstalled);
        Assert.False(vm.CanRemove);
    }

    [AvaloniaFact]
    public async Task Remove_asks_the_service_to_remove_the_current_project()
    {
        var (vm, projects, service) = New();
        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(projects.Current!.Id, service.RemovedProjectId);
    }

    [AvaloniaFact]
    public async Task A_failed_build_shows_plain_words_and_offers_no_install()
    {
        var (vm, _, service) = New();
        service.BuildResult = new BuildOutcome(false, null, "The packing tool is missing from the app.");

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailed);
        Assert.False(vm.CanInstall);
        Assert.Contains("packing tool", vm.FailureWhat + vm.FailureMeaning);
    }

    [AvaloniaFact]
    public async Task A_change_mid_build_discards_the_stale_result()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new ControllableService();
        var vm = new BuildViewModel(changes, projects: projects, service: service);

        var build = vm.BuildCommand.ExecuteAsync(null);
        changes.RecordEdit("AK-15", "Damage", "45", "60", projects.Current!.Id);   // changed mid-build
        service.Complete(new BuildOutcome(true, @"C:\ws\M_v1.0.0_P.pak", null));
        await build;

        Assert.False(vm.IsSucceeded);
        Assert.Null(vm.BuiltPakPath);      // the stale pak is never offered to install
    }

    [AvaloniaFact]
    public async Task Build_sends_the_games_field_name_to_the_service()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "GridInventoryRowSpan", "10", "25", projects.Current!.Id);
        var service = new FakeService();
        var vm = new BuildViewModel(changes, projects: projects, service: service);

        await vm.BuildCommand.ExecuteAsync(null);

        var item = Assert.Single(service.SentItems!);
        Assert.Equal("GridInventoryRowSpan", Assert.Single(item.Changes).Field);
    }

    [AvaloniaFact]
    public async Task A_list_change_is_sent_before_the_value_edits()
    {
        // Adding a slot, then editing a value: the build must apply the list change first, so the
        // edit lands on the slot that now exists.
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Well (base)", "Ingredients[1].Amount.NoSkill", "4", "7", projects.Current!.Id);
        changes.RecordListChange("Well (base)", "Ingredients", ChangeOperation.AddEntry,
            projects.Current!.Id);
        var service = new FakeService();
        var vm = new BuildViewModel(changes, projects: projects, service: service);

        await vm.BuildCommand.ExecuteAsync(null);

        var item = Assert.Single(service.SentItems!);
        Assert.Equal(ChangeOperation.AddEntry, item.Changes[0].Operation);
        Assert.Equal("Ingredients", item.Changes[0].Field);
        Assert.Equal(ChangeOperation.Set, item.Changes[1].Operation);
        Assert.Equal("Ingredients[1].Amount.NoSkill", item.Changes[1].Field);
    }

    private sealed class FakeElevation : IElevation
    {
        public bool IsElevated { get; set; }
        public int Restarts;
        public bool RestartAsAdmin() { Restarts++; return true; }
    }

    private (BuildViewModel vm, FakeService service, FakeElevation elevation) NewWithInstall(
        InstallOutcome installResult, bool elevated = false)
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new FakeService { InstallResult = installResult };
        var elevation = new FakeElevation { IsElevated = elevated };
        var vm = new BuildViewModel(changes, projects: projects, service: service, elevation: elevation);
        return (vm, service, elevation);
    }

    [AvaloniaFact]
    public async Task A_permission_block_offers_restart_as_administrator()
    {
        var (vm, _, elevation) = NewWithInstall(
            new InstallOutcome(false, null, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied));

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailed);
        Assert.True(vm.CanRestartAsAdmin);

        vm.RestartAsAdminCommand.Execute(null);
        Assert.Equal(1, elevation.Restarts);
    }

    [AvaloniaFact]
    public async Task A_busy_file_failure_does_not_offer_restart_as_administrator()
    {
        var (vm, _, _) = NewWithInstall(
            new InstallOutcome(false, null, "SCUM's files were busy.", WriteFailureKind.GameRunning));

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailed);
        Assert.False(vm.CanRestartAsAdmin);
    }

    [AvaloniaFact]
    public async Task A_successful_install_clears_the_restart_button()
    {
        var (vm, service, _) = NewWithInstall(
            new InstallOutcome(false, null, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied));

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.True(vm.CanRestartAsAdmin);

        service.InstallResult = new InstallOutcome(true, "path", null);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.False(vm.CanRestartAsAdmin);
    }

    [AvaloniaFact]
    public async Task A_successful_remove_clears_the_restart_button()
    {
        var (vm, service, _) = NewWithInstall(new InstallOutcome(true, "path", null));
        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        service.RemoveResult = new RemoveOutcome(false, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied);
        await vm.RemoveCommand.ExecuteAsync(null);        // refused → button offered
        Assert.True(vm.CanRestartAsAdmin);

        service.RemoveResult = new RemoveOutcome(true, null);
        await vm.RemoveCommand.ExecuteAsync(null);        // now it works → button gone
        Assert.False(vm.CanRestartAsAdmin);
    }

    [AvaloniaFact]
    public async Task When_already_elevated_there_is_no_restart_button()
    {
        var (vm, _, _) = NewWithInstall(
            new InstallOutcome(false, null, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied),
            elevated: true);

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.False(vm.CanRestartAsAdmin);   // restarting would not gain anything
    }

    [AvaloniaFact]
    public async Task A_permission_block_while_removing_offers_restart_as_administrator()
    {
        var (vm, service, elevation) = NewWithInstall(new InstallOutcome(true, "path", null));
        service.RemoveResult = new RemoveOutcome(false, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied);

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);
        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.True(vm.CanRestartAsAdmin);
        vm.RestartAsAdminCommand.Execute(null);
        Assert.Equal(1, elevation.Restarts);
    }

    [AvaloniaFact]
    public void UseGame_wakes_the_screen_when_the_game_is_configured()
    {
        var vm = new BuildViewModel();                       // practice mode: no service, no launcher
        Assert.False(vm.CanLaunch);
        Assert.True(vm.ShowLaunchNote);

        vm.UseGame(new FakeService(), new FakeLauncher());

        Assert.True(vm.CanLaunch);
        Assert.False(vm.ShowLaunchNote);
    }

    [AvaloniaFact]
    public async Task A_practice_build_is_cleared_when_the_real_engine_arrives()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var vm = new BuildViewModel(changes, projects: projects);      // simulated build
        await vm.BuildCommand.ExecuteAsync(null);
        for (var i = 0; i < 5; i++) vm.Advance();                      // let the practice run finish
        Assert.True(vm.IsSucceeded);

        vm.UseGame(new FakeService(), new FakeLauncher());

        Assert.False(vm.IsSucceeded);                        // back to "ready to build", for real
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public bool IsAvailable { get; set; } = true;
        public int Modded;
        public Task<LaunchResult> LaunchModdedAsync() { Modded++; return Task.FromResult(new LaunchResult(true, null)); }
        public Task<LaunchResult> LaunchUnmoddedAsync() => Task.FromResult(new LaunchResult(true, null));
    }

    [AvaloniaFact]
    public async Task Builds_launch_button_starts_the_modded_game()
    {
        var launcher = new FakeLauncher();
        var vm = new BuildViewModel(launcher: launcher);

        Assert.True(vm.CanLaunch);
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.Modded);
    }
}
