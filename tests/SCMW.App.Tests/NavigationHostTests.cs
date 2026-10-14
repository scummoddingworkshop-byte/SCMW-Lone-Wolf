namespace SCMW.App.Tests;

public class NavigationHostTests
{
    [Fact]
    public void Starts_on_Home()
        => Assert.Equal(ScreenKey.Home, new NavigationHost().Current);

    [Fact]
    public void GoTo_changes_screen_and_raises_Changed()
    {
        var nav = new NavigationHost();
        var raised = 0;
        nav.Changed += () => raised++;

        nav.GoTo(ScreenKey.Workspace);

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void GoTo_same_screen_is_a_noop()
    {
        var nav = new NavigationHost();
        var raised = 0;
        nav.Changed += () => raised++;

        nav.GoTo(ScreenKey.Home);

        Assert.Equal(0, raised);
    }
}
