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
        var window = new MainWindow { DataContext = new ShellViewModel(nav), Height = 1400 };
        window.Show();

        var mods = window.GetVisualDescendants().OfType<ListBoxItem>()
                         .First(i => i.DataContext is NavItem n && n.Key == ScreenKey.Mods);
        var point = mods.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(ScreenKey.Mods, nav.Current);
    }

    [AvaloniaFact]
    public void The_modify_band_holds_the_four_modifying_jobs()
    {
        var vm = new ShellViewModel(new NavigationHost());
        var modify = vm.Bands.Single(b => b.Band == NavBand.Modify);

        var jobs = modify.Items.Take(4).ToList();
        Assert.Equal(new[] { "Items", "Base building", "Recipes", "Cooking" },
                     jobs.Select(i => i.Kind));
        Assert.Equal(new[] { "Modify an item's value", "Modify a base part", "Modify a crafting recipe", "Modify a cooking recipe" },
                     jobs.Select(i => i.Label));
        Assert.All(jobs, i => Assert.NotNull(i.Icon));            // each has its own rail icon
    }

    [AvaloniaFact]
    public void Picking_a_job_row_opens_the_workspace_on_that_list()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        vm.SelectedEntry = vm.Sidebar.First(i => i.Kind == "Recipes" && !i.Creates);

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal("Recipes", vm.Workspace.SelectedKind);

        var modify = vm.Bands.Single(b => b.Band == NavBand.Modify);
        Assert.Equal("Recipes", modify.Selected!.Kind);           // and its row is the lit one
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
    public void The_rail_is_home_and_projects_then_modify_then_make()
    {
        var vm = new ShellViewModel(new NavigationHost());
        var bands = vm.Bands;

        Assert.Equal(new[] { NavBand.Home, NavBand.Modify, NavBand.Make, NavBand.Ship, NavBand.App },
                     bands.Select(b => b.Band));

        // Home and Projects share the unlabelled top group.
        Assert.Equal(new[] { ScreenKey.Home, ScreenKey.Projects }, bands[0].Items.Select(i => i.Key));

        // MODIFY: the four jobs that change what already exists.
        Assert.Equal(new[] { "Items", "Base building", "Recipes", "Cooking" },
                     bands[1].Items.Select(i => i.Kind));
        Assert.All(bands[1].Items, i => Assert.False(i.Creates));

        // MAKE: the four ways to create something new, two of them not built yet.
        Assert.Equal(new[] { "New item", "New base part", "New crafting recipe", "New cooking recipe" },
                     bands[2].Items.Select(i => i.Label));
        Assert.All(bands[2].Items, i => Assert.True(i.Creates));
        Assert.Equal(new[] { false, true, false, true }, bands[2].Items.Select(i => i.ComingSoon));
    }

    [AvaloniaFact]
    public void A_row_that_is_coming_soon_cannot_be_picked()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var soon = vm.Sidebar.First(i => i.Label == "New base part");

        vm.SelectedEntry = soon;

        Assert.False(soon.CanPick);
        Assert.Equal(ScreenKey.Home, nav.Current);   // nothing happened
    }

    [AvaloniaFact]
    public async Task Picking_new_item_opens_the_items_list_ready_to_create()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        vm.SelectedEntry = vm.Sidebar.First(i => i.Label == "New item");

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal("Items", vm.Workspace.SelectedKind);

        // The list is read off-thread, so give it a moment to finish before the panel can open.
        for (var wait = 0; wait < 40 && !vm.Workspace.ShowNewRecipe; wait++)
            await Task.Delay(50);

        Assert.True(vm.Workspace.ShowNewRecipe);
        Assert.Equal("New item", vm.Workspace.NewPanelTitle);
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
