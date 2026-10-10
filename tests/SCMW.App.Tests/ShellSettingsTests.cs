using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class ShellSettingsTests
{
    [AvaloniaFact]
    public void The_settings_area_shows_the_settings_screen()
    {
        var vm = new ShellViewModel(new NavigationHost());
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.IsType<SettingsView>(vm.ActiveView);
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Settings", texts);
        Assert.Contains("UNLOCK KEY", texts);
    }

    [AvaloniaFact]
    public void Settings_state_survives_switching_areas()
    {
        var vm = new ShellViewModel(new NavigationHost());

        vm.Settings.UnlockKey = "KEEP";
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Home);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);

        Assert.Equal("KEEP", vm.Settings.UnlockKey);     // the screen kept its state
        Assert.IsType<SettingsView>(vm.ActiveView);
    }

    [AvaloniaFact]
    public void The_help_area_is_in_the_rail_and_shows_the_help_screen()
    {
        var vm = new ShellViewModel(new NavigationHost());
        Assert.Contains(vm.Sidebar, i => i.Key == ScreenKey.Help);

        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Help);

        Assert.IsType<HelpView>(vm.ActiveView);
    }

    [AvaloniaFact]
    public void The_shell_uses_the_settings_store_it_is_given()
    {
        var store = new InMemorySettingsStore();
        var vm = new ShellViewModel(new NavigationHost(), store);

        vm.Settings.WorkspacePath = @"C:\Somewhere\Else";
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.Equal(@"C:\Somewhere\Else", store.Load().WorkspacePath);
        Assert.IsType<SettingsView>(vm.ActiveView);
        var box = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Name == "WorkspaceBox");
        Assert.Equal(@"C:\Somewhere\Else", box.Text);
    }
}
