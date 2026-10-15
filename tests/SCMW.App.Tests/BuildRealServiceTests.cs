using Avalonia.Headless.XUnit;
using SCMW.Backend.Build;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

/// <summary>
/// Build with the real engine wired in: what it sends the service, where the result lands, and how
/// it reports what it cannot do. Installing, switching off and removing all live on Mods Management,
/// so Build only builds.
/// </summary>
public class BuildRealServiceTests : IDisposable
{
    private readonly List<string> _roots = new();
    public void Dispose() { foreach (var r in _roots) TestProjects.Cleanup(r); }

    private sealed class FakeService : IBuildService
    {
        public BuildOutcome BuildResult = new(true, @"C:\ws\Builds\M\SCMW_M_v1.0.0_P.pak", null);
        public InstallOutcome InstallResult = new(true, @"C:\g\~mods\SCMW_M_v1.0.0_P.pak", null);
        public InstallResult_ Arg;

        public Task<BuildOutcome> BuildAsync(string name, string version, IReadOnlyList<BuildItem> items,
            IProgress<int>? progress, CancellationToken ct)
        {
            SentItems = items;
            progress?.Report(0); progress?.Report(4);
            return Task.FromResult(BuildResult);
        }

        public IReadOnlyList<BuildItem>? SentItems;

        public Task<InstallOutcome> InstallAsync(string id, string name, string version, string pak,
                                                 bool enabled, CancellationToken ct)
        { Arg = new(id, name, version, pak); SentEnabled = enabled; return Task.FromResult(InstallResult); }

        /// <summary>Whether the last copy into SCUM was asked for switched on or off.</summary>
        public bool? SentEnabled;

        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct)
            => Task.FromResult(new RemoveOutcome(true, null));

        public bool IsInstalled(string id) => false;

        public readonly record struct InstallResult_(string Id, string Name, string Version, string Pak);
    }

    private sealed class ControllableService : IBuildService
    {
        private readonly TaskCompletionSource<BuildOutcome> _tcs = new();
        public void Complete(BuildOutcome outcome) => _tcs.TrySetResult(outcome);
        public Task<BuildOutcome> BuildAsync(string n, string v, IReadOnlyList<BuildItem> i, IProgress<int>? p, CancellationToken ct)
            => _tcs.Task;
        public Task<InstallOutcome> InstallAsync(string id, string n, string v, string pak,
                                                 bool enabled, CancellationToken ct)
            => Task.FromResult(new InstallOutcome(true, "path", null));
        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct) => Task.FromResult(new RemoveOutcome(true, null));
        public bool IsInstalled(string id) => false;
    }

    private (BuildViewModel vm, ProjectStore projects, FakeService service) New(
        InstallOutcome? installResult = null)
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new FakeService();
        if (installResult is { } placed) service.InstallResult = placed;
        return (new BuildViewModel(changes, projects: projects, service: service), projects, service);
    }

    [AvaloniaFact]
    public async Task Build_runs_the_real_service_and_remembers_the_pak()
    {
        var (vm, _, _) = New();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsSucceeded);
        Assert.Equal(@"C:\ws\Builds\M\SCMW_M_v1.0.0_P.pak", vm.BuiltPakPath);
    }

    [AvaloniaFact]
    public async Task A_build_puts_the_mod_into_scum_switched_off()
    {
        var (vm, projects, service) = New();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal(projects.Current!.Id, service.Arg.Id);
        Assert.Equal(vm.BuiltPakPath, service.Arg.Pak);
        Assert.False(service.SentEnabled);            // it arrives switched off...
        Assert.Equal("Your mod is built and ready to be enabled in the Mods Management screen.",
                     vm.SuccessMessage);              // ...and the screen says what to do about it
    }

    [AvaloniaFact]
    public async Task Rebuilding_still_arrives_switched_off()
    {
        var (vm, _, service) = New();

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsSucceeded);
        Assert.False(service.SentEnabled);            // a rebuild lands switched off too
    }

    [AvaloniaFact]
    public async Task A_build_that_cannot_be_placed_in_scum_says_so_plainly()
    {
        var (vm, _, _) = New(new InstallOutcome(false, null, "SCUM's files were busy.", WriteFailureKind.GameRunning));

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailed);
        Assert.Contains("could not be put into SCUM", vm.FailureWhat);
        Assert.False(vm.CanRestartAsAdmin);           // a busy file is not a permission block
    }

    [AvaloniaFact]
    public async Task A_permission_block_while_placing_offers_restart_as_administrator()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new FakeService
        {
            InstallResult = new InstallOutcome(false, null, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied),
        };
        var elevation = new FakeElevation();
        var vm = new BuildViewModel(changes, projects: projects, service: service, elevation: elevation);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailed);
        Assert.True(vm.CanRestartAsAdmin);

        vm.RestartAsAdminCommand.Execute(null);
        Assert.Equal(1, elevation.Restarts);
    }

    [AvaloniaFact]
    public async Task A_failed_build_shows_plain_words()
    {
        var (vm, _, service) = New();
        service.BuildResult = new BuildOutcome(false, null, "The packing tool is missing from the app.");

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailed);
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
        Assert.Null(vm.BuiltPakPath);      // the stale pak is never offered
    }

    [AvaloniaFact]
    public async Task A_real_build_backs_up_the_save_first()   // design §11
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var saves = new RecordingSaves();
        var vm = new BuildViewModel(changes, projects: projects, service: new FakeService(), saves: saves);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(saves.BackedUp);
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

    [AvaloniaFact] // a multiply is sent as the finished number, never the factor itself
    public async Task A_multiply_is_sent_as_the_finished_value_not_the_factor()
    {
        var (projects, root) = TestProjects.With("Making objects permanent");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        // The real case: the inventory height (a whole number, 6) scaled by half → 3.
        changes.RecordMultiply("Watering Can ES", "GridInventoryRowSpan", "6", "0.5",
                               projects.Current!.Id, assetPath: "SCMW/Items/Farming/Watering_Can_ES");
        var service = new FakeService();
        var vm = new BuildViewModel(changes, projects: projects, service: service);

        await vm.BuildCommand.ExecuteAsync(null);

        var item = Assert.Single(service.SentItems!);
        var change = Assert.Single(item.Changes);
        Assert.Equal("GridInventoryRowSpan", change.Field);
        Assert.Equal("3", change.NewValue);      // 6 × 0.5, not the factor "0.5"
    }

    private sealed class FakeElevation : IElevation
    {
        public bool IsElevated => false;
        public int Restarts;
        public bool RestartAsAdmin() { Restarts++; return true; }
    }

    [AvaloniaFact]
    public async Task UseGame_wires_the_real_engine_in()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
        var service = new FakeService();
        var vm = new BuildViewModel(changes, projects: projects);      // practice mode first

        vm.UseGame(service);
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(service.SentItems);      // the real service did the work
        Assert.True(vm.IsSucceeded);
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

        vm.UseGame(new FakeService());

        Assert.False(vm.IsSucceeded);                        // back to "ready to build", for real
    }

    /// <summary>A stand-in save service that only records whether a backup was taken.</summary>
    private sealed class RecordingSaves : ISaveBackupsService
    {
        public bool BackedUp { get; private set; }
        public bool IsAvailable => true;
        public string SaveFolder => "";
        public IReadOnlyList<SaveSnapshot> List() => Array.Empty<SaveSnapshot>();
        public string? BackUp() { BackedUp = true; return null; }
        public string? Restore(string snapshotId) => null;
        public string? Delete(string snapshotId) => null;
    }
}
