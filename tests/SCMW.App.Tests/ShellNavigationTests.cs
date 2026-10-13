using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace SCMW.App.Tests;

/// <summary>
/// The shell's navigation model: the three rail bands (Make · Ship · App) and how the
/// rail rows group under them. See the design, §5 Navigation.
/// </summary>
public class ShellNavigationTests
{
    [Fact]
    public void Bands_run_Make_Ship_App_in_order()
        => Assert.Equal(new[] { NavBand.Make, NavBand.Ship, NavBand.App }, NavBands.All);

    [Theory]
    [InlineData(ScreenKey.Workspace, NavBand.Make)]
    [InlineData(ScreenKey.Projects, NavBand.Make)]
    [InlineData(ScreenKey.Build, NavBand.Ship)]
    [InlineData(ScreenKey.Mods, NavBand.Ship)]
    [InlineData(ScreenKey.Backups, NavBand.Ship)]
    [InlineData(ScreenKey.Settings, NavBand.App)]
    [InlineData(ScreenKey.Help, NavBand.App)]
    public void Each_screen_sits_in_its_band(ScreenKey key, NavBand band)
        => Assert.Equal(band, NavBands.For(key));

    [Fact]
    public void A_rail_row_knows_its_band()
        => Assert.Equal(NavBand.Ship, new NavItem(ScreenKey.Build, "Build").Band);

    [Fact]
    public void Screens_group_under_their_band_in_band_order()
    {
        var items = new[]
        {
            new NavItem(ScreenKey.Help, "Help"),
            new NavItem(ScreenKey.Projects, "Projects"),
            new NavItem(ScreenKey.Build, "Build"),
            new NavItem(ScreenKey.Settings, "Settings"),
            new NavItem(ScreenKey.Mods, "Mods"),
            new NavItem(ScreenKey.Backups, "Backups"),
            new NavItem(ScreenKey.Workspace, "Workspace"),
        };

        var groups = NavBands.Group(items);

        Assert.Equal(new[] { NavBand.Make, NavBand.Ship, NavBand.App },
                     groups.Select(g => g.Band));
        Assert.Equal(new[] { ScreenKey.Workspace, ScreenKey.Projects },
                     groups[0].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups },
                     groups[1].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Settings, ScreenKey.Help },
                     groups[2].Items.Select(i => i.Key));
    }

    [AvaloniaFact]
    public void The_rail_groups_its_rows_into_the_three_bands()
    {
        var vm = new ShellViewModel(new NavigationHost());

        Assert.Equal(new[] { NavBand.Make, NavBand.Ship, NavBand.App }, vm.Bands.Select(b => b.Band));
        Assert.Equal(
            new[] { ScreenKey.Home, ScreenKey.Projects, ScreenKey.Items, ScreenKey.BaseBuilding, ScreenKey.Recipes },
            vm.Bands[0].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups },
                     vm.Bands[1].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Settings, ScreenKey.Help },
                     vm.Bands[2].Items.Select(i => i.Key));
    }

    [AvaloniaFact]
    public void Only_one_band_row_is_selected_at_a_time()
    {
        var vm = new ShellViewModel(new NavigationHost());

        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);

        var selected = vm.Bands.Where(b => b.Selected is not null).ToList();
        Assert.Single(selected);
        Assert.Equal(ScreenKey.Mods, selected[0].Selected!.Key);
    }

    [AvaloniaFact]
    public void Choosing_a_band_row_navigates_to_it()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        var ship = vm.Bands.Single(b => b.Band == NavBand.Ship);
        ship.Selected = ship.Items.Single(i => i.Key == ScreenKey.Build);

        Assert.Equal(ScreenKey.Build, nav.Current);
        Assert.Same(ship.Selected, vm.SelectedEntry);
    }

    [AvaloniaFact]
    public void The_rail_shows_the_three_band_labels()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("MAKE", labels);
        Assert.Contains("SHIP", labels);
        Assert.Contains("APP", labels);
    }

    [AvaloniaFact]
    public void The_shell_shows_the_on_the_way_strip()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains(OnTheWay.Message, texts);
    }
}
