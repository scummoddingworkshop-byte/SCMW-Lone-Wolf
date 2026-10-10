using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class ShellProjectsTests
{
    [AvaloniaFact]
    public void Opening_projects_shows_the_projects_screen()
    {
        var (store, root) = TestProjects.With("Duct Tape Expanded");
        try
        {
            var nav = new NavigationHost();
            var vm = new ShellViewModel(nav, projects: store);

            // Select Projects before the window is shown, so the view is laid out on show.
            vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Projects);

            var window = new MainWindow { DataContext = vm };
            window.Show();

            Assert.IsType<ProjectsView>(vm.ActiveView);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Duct Tape Expanded", texts);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Projects_state_survives_switching_areas()
    {
        var vm = new ShellViewModel(new NavigationHost());

        vm.Projects.NewProjectCommand.Execute(null);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Home);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Projects);

        Assert.True(vm.Projects.IsPanelOpen);
        Assert.IsType<ProjectsView>(vm.ActiveView);
    }
}
