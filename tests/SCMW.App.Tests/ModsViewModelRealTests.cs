using Avalonia.Headless.XUnit;
using SCMW.Backend.Build;

namespace SCMW.App.Tests;

public class ModsViewModelRealTests
{
    private sealed class FakeElevation : IElevation
    {
        public bool IsElevated { get; set; }
        public int Restarts;
        public bool RestartAsAdmin() { Restarts++; return true; }
    }

    private sealed class FakeMods : IModsService
    {
        public bool IsAvailable { get; set; } = true;
        public readonly List<ModsEntry> Entries = new();
        public readonly List<(string File, bool Enabled)> Toggles = new();
        public readonly List<string> Removed = new();
        public readonly List<IReadOnlyList<string>> Imports = new();
        public ModsResult SetResult = new(true, null);
        public ImportOutcome? ImportOverride;

        public IReadOnlyList<ModsEntry> List() => Entries.ToList();
        public ModsResult SetEnabled(string fileName, bool enabled)
        { Toggles.Add((fileName, enabled)); return SetResult; }
        public ModsResult SetAllEnabled(bool enabled) => new(true, null);
        public ModsResult Remove(string fileName) { Removed.Add(fileName); return SetResult; }
        public ImportOutcome Import(IReadOnlyList<string> sourcePaths)
        {
            Imports.Add(sourcePaths);
            if (ImportOverride is { } over) return over;
            foreach (var path in sourcePaths)
                Entries.Add(new ModsEntry(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), true, true));
            return new ImportOutcome(true, null, sourcePaths.Count, 0);
        }
    }

    [AvaloniaFact]
    public void UseService_wakes_the_screen_when_the_game_is_configured()
    {
        var vm = new ModsViewModel();                    // preview mode: no service
        Assert.True(vm.ShowEmptyState);

        var svc = new FakeMods();
        svc.Entries.Add(new ModsEntry("A.pak", "A", true, true));
        vm.UseService(svc);

        Assert.Single(vm.Mods);
        Assert.False(vm.ShowEmptyState);
    }

    [AvaloniaFact]
    public void The_list_and_summary_come_from_the_service()
    {
        var svc = new FakeMods();
        svc.Entries.Add(new ModsEntry("SCMW_A_v1.0.0_P.pak", "SCMW_A_v1.0.0_P", true, false));
        svc.Entries.Add(new ModsEntry("B.pak.disabled", "B", false, true));

        var vm = new ModsViewModel(service: svc);

        Assert.Equal(2, vm.Mods.Count);
        Assert.True(vm.HasMods);
        Assert.Equal("2 mods installed · 1 switched on.", vm.Summary);
    }

    [AvaloniaFact]
    public void Switching_a_mod_calls_the_service_with_its_file_name()
    {
        var svc = new FakeMods();
        svc.Entries.Add(new ModsEntry("B.pak", "B", true, true));
        var vm = new ModsViewModel(service: svc);

        vm.Mods.Single().IsOn = false;                 // the row's switch

        Assert.Equal(("B.pak", false), svc.Toggles.Single());
    }

    [AvaloniaFact]
    public void A_refused_switch_flips_the_row_back()
    {
        var svc = new FakeMods { SetResult = new ModsResult(false, "Close the game, then try again.") };
        svc.Entries.Add(new ModsEntry("B.pak", "B", true, true));
        var vm = new ModsViewModel(service: svc);

        vm.Mods.Single().IsOn = false;

        Assert.True(vm.Mods.Single().IsOn);            // reverted
        Assert.Contains("Close the game", vm.Message);
    }

    [AvaloniaFact]
    public void Confirm_remove_asks_the_service_to_remove_that_file()
    {
        var svc = new FakeMods();
        svc.Entries.Add(new ModsEntry("B.pak", "B", true, true));
        var vm = new ModsViewModel(service: svc);

        vm.ConfirmRemoveCommand.Execute(vm.Mods.Single());

        Assert.Equal("B.pak", svc.Removed.Single());
    }

    [AvaloniaFact]
    public void Importing_dropped_paths_adds_them_and_refreshes()
    {
        var svc = new FakeMods();
        var vm = new ModsViewModel(service: svc);

        vm.ImportDropped(new[] { @"C:\Downloads\New.pak" });

        Assert.Single(svc.Imports);                       // the view model really asked the service
        var mod = Assert.Single(vm.Mods);                 // and the refreshed list shows it
        Assert.Equal("New", mod.Name);
    }

    [AvaloniaFact]
    public void A_refused_import_says_why()
    {
        var svc = new FakeMods { ImportOverride = new ImportOutcome(false, null, 0, 1, 0) };
        var vm = new ModsViewModel(service: svc);

        vm.ImportDropped(new[] { @"C:\Downloads\Dup.pak" });

        Assert.Contains("already installed", vm.Message);
    }

    [AvaloniaFact]
    public void With_no_game_the_screen_says_so()    {
        var svc = new FakeMods { IsAvailable = false };
        var vm = new ModsViewModel(service: svc);

        Assert.Contains("Settings", vm.Message);
    }

    [AvaloniaFact]
    public void Going_to_the_mods_screen_refreshes_the_list()
    {
        var svc = new FakeMods();
        var vm = new ShellViewModel(new NavigationHost(), modsService: svc);
        svc.Entries.Add(new ModsEntry("A.pak", "A", true, true));   // a mod installed after startup

        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);

        Assert.Single(vm.Mods.Mods);
    }

    [AvaloniaFact]
    public void A_permission_block_on_a_switch_offers_restart_as_administrator()
    {
        var svc = new FakeMods
        {
            SetResult = new ModsResult(false, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied)
        };
        svc.Entries.Add(new ModsEntry("B.pak", "B", true, true));
        var elevation = new FakeElevation();
        var vm = new ModsViewModel(service: svc, elevation: elevation);

        vm.Mods.Single().IsOn = false;

        Assert.True(vm.CanRestartAsAdmin);
        vm.RestartAsAdminCommand.Execute(null);
        Assert.Equal(1, elevation.Restarts);
    }

    [AvaloniaFact]
    public void A_busy_switch_does_not_offer_restart_as_administrator()
    {
        var svc = new FakeMods
        {
            SetResult = new ModsResult(false, "SCUM's files were busy.", WriteFailureKind.GameRunning)
        };
        svc.Entries.Add(new ModsEntry("B.pak", "B", true, true));
        var vm = new ModsViewModel(service: svc, elevation: new FakeElevation());

        vm.Mods.Single().IsOn = false;

        Assert.False(vm.CanRestartAsAdmin);
    }

    [AvaloniaFact]
    public void A_permission_block_on_an_import_offers_restart_as_administrator()
    {
        var svc = new FakeMods
        {
            ImportOverride = new ImportOutcome(false, "Windows blocked SCMW.", 0, 0, 0,
                WriteFailureKind.PermissionDenied)
        };
        var vm = new ModsViewModel(service: svc, elevation: new FakeElevation());

        vm.ImportDropped(new[] { @"C:\Downloads\New.pak" });

        Assert.True(vm.CanRestartAsAdmin);
    }

    [AvaloniaFact]
    public void A_successful_switch_clears_the_restart_button()
    {
        var svc = new FakeMods
        {
            SetResult = new ModsResult(false, "Windows blocked SCMW.", WriteFailureKind.PermissionDenied)
        };
        svc.Entries.Add(new ModsEntry("B.pak", "B", true, true));
        var vm = new ModsViewModel(service: svc, elevation: new FakeElevation());

        vm.Mods.Single().IsOn = false;            // refused → button offered
        Assert.True(vm.CanRestartAsAdmin);

        svc.SetResult = new ModsResult(true, null);
        vm.Mods.Single().IsOn = false;            // now it works → button gone
        Assert.False(vm.CanRestartAsAdmin);
    }
}
