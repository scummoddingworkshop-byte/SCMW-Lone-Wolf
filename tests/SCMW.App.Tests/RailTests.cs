using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace SCMW.App.Tests;

public class RailTests
{
    [AvaloniaFact]
    public void Rail_rows_contain_no_button()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var railItems = window.GetVisualDescendants().OfType<ListBoxItem>();

        Assert.All(railItems, item => Assert.Empty(item.GetVisualDescendants().OfType<Button>()));
    }

    [AvaloniaFact]
    public void The_rail_has_a_base_building_area()
    {
        var vm = new ShellViewModel(new NavigationHost());

        var entry = vm.Sidebar.Single(i => i.Key == ScreenKey.BaseBuilding);

        Assert.Equal("Base building", entry.Label);
        Assert.NotNull(entry.Icon);
    }

    [AvaloniaFact]
    public void The_rail_has_a_recipes_area()
    {
        var vm = new ShellViewModel(new NavigationHost());

        var entry = vm.Sidebar.Single(i => i.Key == ScreenKey.Recipes);

        Assert.Equal("Recipes", entry.Label);
        Assert.NotNull(entry.Icon);
    }

    [AvaloniaFact]
    public void The_rail_lists_the_workspace_first_under_make()
    {
        var vm = new ShellViewModel(new NavigationHost());

        var first = vm.Sidebar.First();
        Assert.Equal(ScreenKey.Workspace, first.Key);
        Assert.Equal("Workspace", first.Label);
        Assert.NotNull(first.Icon);

        var make = vm.Bands.Single(b => b.Band == NavBand.Make);
        Assert.Equal(ScreenKey.Workspace, make.Items.First().Key);
    }

    [AvaloniaFact]
    public void Clicking_a_rail_row_changes_the_shown_area()
    {
        var nav = new NavigationHost();
        var window = new MainWindow { DataContext = new ShellViewModel(nav), Height = 900 };
        window.Show();

        var last = window.GetVisualDescendants().OfType<ListBoxItem>().Last();
        var point = last.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(ScreenKey.Help, nav.Current);   // the last rail row
    }

    [AvaloniaFact]
    public void The_selected_rail_row_shows_exactly_one_icon()
    {
        var vm = new ShellViewModel(new NavigationHost());
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.All(vm.Sidebar, item => Assert.NotNull(item.Icon));   // the icons loaded

        var home = window.GetVisualDescendants().OfType<ListBoxItem>()
                          .Single(i => i.DataContext is NavItem n && n.Key == ScreenKey.Home);
        Assert.True(home.IsSelected);                                // Home starts selected

        var images = home.GetVisualDescendants().OfType<Image>().ToList();
        Assert.Equal(2, images.Count);                               // a light and a dark icon
        Assert.Single(images.Where(i => i.IsVisible));               // only the dark one shows
    }
}
