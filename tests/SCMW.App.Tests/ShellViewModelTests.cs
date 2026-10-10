using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ShellViewModelTests
{
    [AvaloniaFact]
    public void Sidebar_lists_the_six_areas_in_order()
    {
        var vm = new ShellViewModel(new NavigationHost());

        Assert.Equal(
            new[]
            {
                ScreenKey.Home, ScreenKey.Projects, ScreenKey.Items,
                ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups, ScreenKey.Settings, ScreenKey.Help,
            },
            vm.Sidebar.Select(i => i.Key));
    }

    [AvaloniaFact]
    public void Has_modded_and_unmodded_launch_commands()
    {
        var vm = new ShellViewModel(new NavigationHost());

        Assert.NotNull(vm.LaunchModdedCommand);
        Assert.NotNull(vm.LaunchUnmoddedCommand);
    }

    [AvaloniaFact]
    public void Launch_is_disabled_without_a_game()
    {
        var vm = new ShellViewModel(new NavigationHost());

        Assert.False(vm.CanLaunch);
        Assert.False(vm.LaunchModdedCommand.CanExecute(null));
        Assert.False(vm.LaunchUnmoddedCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Selecting_an_area_navigates()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);

        Assert.Equal(ScreenKey.Mods, nav.Current);
        Assert.Equal("Mods", vm.ScreenTitle);
    }
}
