using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class HomeViewModelTests
{
    [AvaloniaFact]
    public void Quick_actions_navigate_to_their_area()
    {
        var nav = new NavigationHost();
        var vm = new HomeViewModel(nav);

        vm.NewProjectCommand.Execute(null);
        Assert.Equal(ScreenKey.Projects, nav.Current);

        vm.OpenProjectCommand.Execute(null);
        Assert.Equal(ScreenKey.Projects, nav.Current);

        vm.BrowseItemsCommand.Execute(null);
        Assert.Equal(ScreenKey.Items, nav.Current);

        vm.BuildPlayCommand.Execute(null);
        Assert.Equal(ScreenKey.Build, nav.Current);
    }

    [AvaloniaFact]
    public void Opening_a_recent_project_goes_to_items()
    {
        var nav = new NavigationHost();
        var vm = new HomeViewModel(nav);

        vm.OpenRecentProjectCommand.Execute(vm.RecentProjects[0]);

        Assert.Equal(ScreenKey.Items, nav.Current);
    }

    [AvaloniaFact]
    public void Home_has_sample_projects_by_default()
    {
        var vm = new HomeViewModel(new NavigationHost());

        Assert.True(vm.HasProjects);
        Assert.NotEmpty(vm.RecentProjects);
    }

    [AvaloniaFact]
    public void Home_shows_no_projects_when_the_list_is_empty()
    {
        var vm = new HomeViewModel(new NavigationHost(), Array.Empty<ProjectSummary>());

        Assert.False(vm.HasProjects);
        Assert.Empty(vm.RecentProjects);
    }

    [AvaloniaFact]
    public void Welcome_starts_visible_and_can_be_dismissed()
    {
        var vm = new HomeViewModel(new NavigationHost());

        Assert.True(vm.ShowWelcome);

        vm.DismissWelcomeCommand.Execute(null);

        Assert.False(vm.ShowWelcome);
    }

    [AvaloniaFact]
    public void The_first_run_card_sends_new_players_to_settings()
    {
        var nav = new NavigationHost();
        var vm = new HomeViewModel(nav);

        vm.StartWelcomeCommand.Execute(null);

        Assert.Equal(ScreenKey.Settings, nav.Current);
    }

    [AvaloniaFact]
    public void The_first_run_card_hides_once_scum_is_set_up()
    {
        var vm = new HomeViewModel(new NavigationHost(), gameReady: () => true);

        Assert.False(vm.ShowWelcome);          // nothing to nudge once the game is configured

        var fresh = new HomeViewModel(new NavigationHost(), gameReady: () => false);
        Assert.True(fresh.ShowWelcome);
    }

    [AvaloniaFact]
    public void Refreshing_the_welcome_card_follows_the_game_state()
    {
        var ready = false;
        var vm = new HomeViewModel(new NavigationHost(), gameReady: () => ready);
        Assert.True(vm.ShowWelcome);

        ready = true;                          // the player set SCUM up on the Settings screen
        vm.RefreshWelcome();

        Assert.False(vm.ShowWelcome);
    }
}
