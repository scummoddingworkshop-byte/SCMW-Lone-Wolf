using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

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
    [InlineData(ScreenKey.BulkEdit, NavBand.Make)]
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
            new[]
            {
                ScreenKey.Workspace,
                ScreenKey.BulkEdit,
                ScreenKey.Home, ScreenKey.Projects,
            },
            vm.Bands[0].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Preview, ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups },
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

    [AvaloniaFact]
    public void The_strip_explains_the_levels_in_plain_words()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
                          .Select(t => t.Text ?? "").ToList();

        // A level must say what it is, not just its number.
        Assert.Contains(texts, t => t.Contains("Change items values") && t.Contains("Mod level 1"));
        // "Coming up" starts on its own, after a blank line.
        Assert.Contains(texts, t => t.Contains("\n\nComing up"));
    }

    [AvaloniaFact]
    public void The_strip_can_be_dismissed()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var strip = window.GetVisualDescendants().OfType<Border>()
                          .Single(b => b.Classes.Contains("ontheway"));
        Assert.True(strip.IsVisible);

        window.GetVisualDescendants().OfType<Button>()
              .Single(b => b.Name == "DismissOnTheWay")
              .Command!.Execute(null);

        Assert.False(strip.IsVisible);
    }

    [AvaloniaFact]
    public void Band_headers_are_legible_with_breathing_room()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var make = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "MAKE");

        Assert.True(make.FontSize >= 13, $"band header too small: {make.FontSize}");
        Assert.True(make.Margin.Top >= 8, $"band header has too little space above: {make.Margin.Top}");
    }

    [AvaloniaFact]
    public void The_workspace_area_shows_the_workspace_view()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        nav.GoTo(ScreenKey.Workspace);

        Assert.IsType<WorkspaceView>(vm.ActiveView);
    }

    [AvaloniaFact]
    public void The_change_many_area_shows_the_bulk_table_view()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        nav.GoTo(ScreenKey.BulkEdit);

        Assert.IsType<BulkTableView>(vm.ActiveView);
    }
}
