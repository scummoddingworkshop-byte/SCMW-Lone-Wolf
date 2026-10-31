using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class ShellProjectsTests
{
    [AvaloniaFact]
    public void Clicking_a_project_card_picks_it_and_stays_on_projects()
    {
        var (store, root) = TestProjects.With("Duct Tape Expanded");
        try
        {
            var nav = new NavigationHost();
            var vm = new ShellViewModel(nav, projects: store);
            var window = new MainWindow { DataContext = vm };
            window.Width = 1200;
            window.Height = 1080;
            window.Show();

            var tile = window.GetVisualDescendants().OfType<ProjectTile>().First();
            var onCard = tile.TranslatePoint(new Point(20, 20), window)!.Value;

            window.MouseDown(onCard, MouseButton.Left);
            window.MouseUp(onCard, MouseButton.Left);

            Assert.Equal(ScreenKey.Projects, nav.Current);          // never leaves the screen
            Assert.Equal(store.All().Single().Id, store.Current!.Id);
        }
        finally { TestProjects.Cleanup(root); }
    }

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
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Projects);

        Assert.True(vm.Projects.IsPanelOpen);
        Assert.IsType<ProjectsView>(vm.ActiveView);
    }
}
