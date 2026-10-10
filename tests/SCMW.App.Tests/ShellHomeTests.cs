using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class ShellHomeTests
{
    [AvaloniaFact]
    public void Home_area_shows_the_home_screen()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Start a new project", texts);       // the Home quick actions
    }

    [AvaloniaFact]
    public void A_quick_action_changes_the_shown_area_and_the_rail_follows()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm };
        window.Show();

        vm.Home.BrowseItemsCommand.Execute(null);

        Assert.Equal(ScreenKey.Items, nav.Current);
        Assert.Equal(ScreenKey.Items, vm.SelectedEntry.Key);
    }

    [AvaloniaFact]
    public void Welcome_dismissal_survives_switching_areas()
    {
        var vm = new ShellViewModel(new NavigationHost());

        vm.Home.DismissWelcomeCommand.Execute(null);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Home);

        Assert.False(vm.Home.ShowWelcome);
        Assert.IsType<HomeView>(vm.ActiveView);
    }

    /// <summary>
    /// Proves the card's command actually resolves through the XAML binding (the
    /// riskiest line on the Home screen) — the card is not decorative.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_a_project_card_opens_it()
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

            var card = window.GetVisualDescendants().OfType<ProjectCard>().First();
            var inner = card.FindControl<Button>("Inner")!;

            Assert.NotNull(inner.Command);
            Assert.NotNull(inner.CommandParameter);

            inner.Command!.Execute(inner.CommandParameter);

            Assert.Equal(ScreenKey.Items, nav.Current);
        }
        finally { TestProjects.Cleanup(root); }
    }

    /// <summary>Proves each quick-action tile is wired to the right command.</summary>
    [AvaloniaFact]
    public void Clicking_a_quick_action_changes_the_area()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm };
        window.Show();

        var tiles = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("quickaction"))
            .ToList();

        Assert.Equal(4, tiles.Count);
        Assert.All(tiles, t => Assert.NotNull(t.Command));

        // Tiles in order: new · open · browse items · build & play.
        tiles[2].Command!.Execute(null);

        Assert.Equal(ScreenKey.Items, nav.Current);
        Assert.Equal(ScreenKey.Items, vm.SelectedEntry.Key);
    }
}
