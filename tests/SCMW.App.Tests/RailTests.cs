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
    public void The_rail_lists_home_first_and_the_workspace_first_under_make()
    {
        var vm = new ShellViewModel(new NavigationHost());

        var first = vm.Sidebar.First();
        Assert.Equal(ScreenKey.Home, first.Key);
        Assert.Equal("Home", first.Label);
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

        var mods = window.GetVisualDescendants().OfType<ListBoxItem>()
                         .First(i => i.DataContext is NavItem n && n.Key == ScreenKey.Mods);
        var point = mods.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(ScreenKey.Mods, nav.Current);
    }

    [AvaloniaFact]
    public void The_make_band_offers_the_four_jobs_where_the_workspace_row_was()
    {
        var vm = new ShellViewModel(new NavigationHost());
        var make = vm.Bands.Single(b => b.Band == NavBand.Make);

        var jobs = make.Items.Take(4).ToList();
        Assert.Equal(new[] { "Items", "Base building", "Recipes", "Cooking" },
                     jobs.Select(i => i.Kind));
        Assert.Equal(new[] { "Modify an item's value", "Modify a base part", "Modify a crafting recipe", "Modify a cooking recipe" },
                     jobs.Select(i => i.Label));
        Assert.All(jobs, i => Assert.NotNull(i.Icon));            // each has its own rail icon
        Assert.Equal(ScreenKey.Projects, make.Items[4].Key);      // Projects still follows them
    }

    [AvaloniaFact]
    public void Picking_a_job_row_opens_the_workspace_on_that_list()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        vm.SelectedEntry = vm.Sidebar.First(i => i.Kind == "Recipes");

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal("Recipes", vm.Workspace.SelectedKind);

        var make = vm.Bands.Single(b => b.Band == NavBand.Make);
        Assert.Equal("Recipes", make.Selected!.Kind);             // and its row is the lit one
    }

    [AvaloniaFact]
    public void The_rail_is_wide_enough_for_the_longest_job_label()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Width = 1200;
        window.Height = 900;
        window.Show();
        window.UpdateLayout();

        var rail = window.FindControl<Border>("Rail");
        Assert.NotNull(rail);
        Assert.True(rail!.Bounds.Width >= 240, $"the rail was only {rail.Bounds.Width} wide");

        // The longest row's words fit inside the space the row gives them, so nothing is cut off.
        var cooking = window.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => i.DataContext is NavItem n && n.Kind == "Cooking");
        var label = cooking.GetVisualDescendants().OfType<TextBlock>().First();
        label.Measure(Size.Infinity);
        var needed = label.DesiredSize;

        Assert.True(needed.Width <= label.Bounds.Width + 1,
                    $"\"{label.Text}\" needs {needed.Width:0} but has {label.Bounds.Width:0}");
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
