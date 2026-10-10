using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ModsViewModelTests
{
    [AvaloniaFact]
    public void Shows_the_list_and_summary_from_the_store()
    {
        var store = new ModList();
        store.Install("My first mod");
        store.Add("SomeMod.pak");
        var vm = new ModsViewModel(store);

        Assert.True(vm.HasMods);
        Assert.False(vm.ShowEmptyState);
        Assert.Equal(2, vm.Mods.Count);
        Assert.Equal("2 mods installed · 2 switched on.", vm.Summary);
    }

    [AvaloniaFact]
    public void Switching_a_mod_off_updates_the_summary_and_keeps_it()
    {
        var store = new ModList();
        var mod = store.Add("SomeMod.pak");
        var vm = new ModsViewModel(store);

        mod.IsOn = false;

        Assert.Equal("1 mod installed · 0 switched on.", vm.Summary);
        Assert.Contains(mod, vm.Mods);        // still installed
    }

    [AvaloniaFact]
    public void Remove_asks_first_and_cancel_keeps_the_mod()
    {
        var store = new ModList();
        var mod = store.Add("SomeMod.pak");
        var vm = new ModsViewModel(store);

        vm.RequestRemoveCommand.Execute(mod);
        Assert.True(mod.IsConfirmingRemove);
        Assert.Contains(mod, vm.Mods);

        vm.CancelRemoveCommand.Execute(mod);
        Assert.False(mod.IsConfirmingRemove);
        Assert.Contains(mod, vm.Mods);
    }

    [AvaloniaFact]
    public void Confirm_remove_takes_the_mod_out()
    {
        var store = new ModList();
        var mod = store.Add("SomeMod.pak");
        var vm = new ModsViewModel(store);

        vm.ConfirmRemoveCommand.Execute(mod);

        Assert.DoesNotContain(mod, vm.Mods);
        Assert.True(vm.ShowEmptyState);
    }

    [AvaloniaFact]
    public void Adding_a_mod_adds_an_external_one()
    {
        var vm = new ModsViewModel();

        vm.AddModCommand.Execute(null);

        var mod = Assert.Single(vm.Mods);
        Assert.True(mod.IsExternal);
    }

    [AvaloniaFact]
    public void Go_to_build_navigates()
    {
        var nav = new NavigationHost();
        var vm = new ModsViewModel(new ModList(), nav);

        vm.GoToBuildCommand.Execute(null);

        Assert.Equal(ScreenKey.Build, nav.Current);
    }
}
