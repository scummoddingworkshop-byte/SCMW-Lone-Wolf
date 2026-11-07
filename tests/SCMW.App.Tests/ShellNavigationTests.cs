using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
    public void Bands_run_Top_Modify_Make_Ship_App_in_order()
        => Assert.Equal(new[] { NavBand.Top, NavBand.Modify, NavBand.Make, NavBand.Ship, NavBand.App },
                        NavBands.All);

    [Theory]
    [InlineData(ScreenKey.Projects, NavBand.Top)]
    [InlineData(ScreenKey.Workspace, NavBand.Modify)]
    [InlineData(ScreenKey.BulkEdit, NavBand.Modify)]
    [InlineData(ScreenKey.Mods, NavBand.Ship)]
    [InlineData(ScreenKey.Backups, NavBand.Ship)]
    [InlineData(ScreenKey.Settings, NavBand.App)]
    [InlineData(ScreenKey.Help, NavBand.App)]
    public void Each_screen_sits_in_its_band(ScreenKey key, NavBand band)
        => Assert.Equal(band, NavBands.For(key));

    [Fact]
    public void A_rail_row_knows_its_band()
        => Assert.Equal(NavBand.Ship, new NavItem(ScreenKey.Mods, "Mods Management").Band);

    [Fact]
    public void Screens_group_under_their_band_in_band_order()
    {
        var items = new[]
        {
            new NavItem(ScreenKey.Help, "Help"),
            new NavItem(ScreenKey.Projects, "Projects"),
            new NavItem(ScreenKey.Settings, "Settings"),
            new NavItem(ScreenKey.Mods, "Mods Management"),
            new NavItem(ScreenKey.Backups, "Backups"),
            new NavItem(ScreenKey.Workspace, "Modify an item's value"),
        };

        var groups = NavBands.Group(items);

        // Bands come in rail order; the rows inside a band keep the order they were given.
        Assert.Equal(new[] { NavBand.Top, NavBand.Modify, NavBand.Ship, NavBand.App },
                     groups.Select(g => g.Band));
        Assert.Equal(new[] { "Projects" }, groups[0].Items.Select(i => i.Label));
        Assert.Equal(new[] { "Modify an item's value" }, groups[1].Items.Select(i => i.Label));
        Assert.Equal(new[] { ScreenKey.Mods, ScreenKey.Backups },
                     groups[2].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Help, ScreenKey.Settings },   // the order they were given
                     groups[3].Items.Select(i => i.Key));
    }

    [AvaloniaFact]
    public void The_rail_groups_its_rows_into_bands()
    {
        var vm = new ShellViewModel(new NavigationHost());

        Assert.Equal(new[] { NavBand.Top, NavBand.Modify, NavBand.Make, NavBand.Ship, NavBand.App },
                     vm.Bands.Select(b => b.Band));
        Assert.Equal(new[] { ScreenKey.Projects }, vm.Bands[0].Items.Select(i => i.Key));
        Assert.All(vm.Bands[1].Items, i => Assert.False(i.Creates));
        Assert.All(vm.Bands[2].Items, i => Assert.True(i.Creates));
        Assert.Equal(new[] { ScreenKey.Mods, ScreenKey.Backups },
                     vm.Bands[3].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Settings, ScreenKey.Help },
                     vm.Bands[4].Items.Select(i => i.Key));
    }

    [AvaloniaFact] // a MAKE row lights alone while its panel is open — never beside the Modify row
    public void A_make_row_is_the_only_lit_row_while_its_panel_is_open()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        var modifyBand = vm.Bands.Single(b => b.Band == NavBand.Modify);
        var makeBand = vm.Bands.Single(b => b.Band == NavBand.Make);
        var modifyItems = vm.Sidebar.Single(i => i.Key == ScreenKey.Workspace && i.Kind == "Items" && !i.Creates);
        var makeRecipe = vm.Sidebar.Single(i => i.Creates && i.Kind == "Recipes");

        // Stand on the Workspace, browsing Items — the Modify row is the lit one.
        modifyBand.Selected = modifyItems;
        Assert.Equal(ScreenKey.Workspace, nav.Current);

        // Click "New crafting recipe": that row alone is lit while its panel is open.
        makeBand.Selected = makeRecipe;

        Assert.True(vm.Workspace.ShowNewRecipe);
        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.Same(makeRecipe, vm.SelectedEntry);

        // Closing the panel puts the light back on the Modify row for the open list.
        vm.Workspace.CancelNewRecipeCommand.Execute(null);

        Assert.False(vm.Workspace.ShowNewRecipe);
        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.Same(vm.Sidebar.Single(i => i.Kind == "Recipes" && !i.Creates), vm.SelectedEntry);

        // The row stays usable: going back to Items and clicking it again still switches the list.
        modifyBand.Selected = modifyItems;
        Assert.Equal("Items", vm.Workspace.SelectedKind);

        makeBand.Selected = makeRecipe;
        Assert.Equal("Recipes", vm.Workspace.SelectedKind);
        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.Same(makeRecipe, vm.SelectedEntry);
    }

    [AvaloniaFact] // the owner's case: the list is already open, then "New crafting recipe" is clicked
    public void Clicking_a_make_row_for_the_list_already_open_lights_that_row_alone()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);

        nav.GoTo(ScreenKey.Workspace);           // stand on the Workspace
        vm.Workspace.SelectedKind = "Recipes";   // with Recipes already the open list

        var makeRow = vm.Sidebar.Single(i => i.Creates && i.Kind == "Recipes");
        var makeBand = vm.Bands.Single(b => b.Band == NavBand.Make);

        makeBand.Selected = makeRow;             // the click the rail makes

        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.Same(makeRow, vm.SelectedEntry);

        // A "coming soon" idea may be clicked too: it lights nothing of its own, and the panel's row
        // keeps the light.
        var soon = vm.Sidebar.First(i => i.ComingSoon);
        vm.Bands.Single(b => b.Band == NavBand.Make).Selected = soon;

        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.Same(makeRow, vm.SelectedEntry);
    }

    [AvaloniaFact] // the owner's path with real clicks: Recipes was the list already open
    public void Clicking_new_crafting_recipe_when_recipes_is_open_lights_one_row()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        window.UpdateLayout();

        ClickRailRow(window, "Modify a crafting recipe");
        ClickRailRow(window, "New crafting recipe");
        Assert.True(LitRows(window) == 1, Describe(window, vm));

        ClickRailRow(window, "New crafting recipe");   // the click that used to do nothing
        Assert.True(LitRows(window) == 1, Describe(window, vm));
        Assert.Equal("Recipes", vm.Workspace.SelectedKind);
    }

    [AvaloniaFact] // the owner's report: a real click on "New item" must light THAT row, not Modify
    public void Clicking_new_item_in_the_rail_lights_that_row()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        window.UpdateLayout();

        ClickRailRow(window, "Modify an item's value");
        ClickRailRow(window, "New item");
        window.UpdateLayout();

        Assert.True(vm.Workspace.ShowNewRecipe);          // the panel stays open
        Assert.Equal(new[] { "New item" }, LitLabels(window));
    }

    [AvaloniaFact] // the owner's report: "Modify an item's value" must put the New form away again
    public void Going_from_new_item_to_modify_closes_the_new_panel()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        window.UpdateLayout();

        ClickRailRow(window, "New item");
        window.UpdateLayout();
        Assert.True(vm.Workspace.ShowNewRecipe);

        ClickRailRow(window, "Modify an item's value");
        window.UpdateLayout();

        Assert.False(vm.Workspace.ShowNewRecipe);                       // the New form is gone
        Assert.True(vm.Workspace.ShowEmptyPrompt);                      // the invitation is back
        Assert.Equal(new[] { "Modify an item's value" }, LitLabels(window));
    }

    /// <summary>The labels of the rail rows that look selected in the real window.</summary>
    private static string[] LitLabels(MainWindow window) =>
        window.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.IsSelected && i.FindAncestorOfType<ListBox>()?.DataContext is NavBandSection)
            .Select(i => (i.DataContext as NavItem)?.Label ?? "?")
            .ToArray();

    [AvaloniaFact] // a real click on a MAKE row must leave exactly one row lit, and stay usable
    public void Clicking_a_make_row_leaves_one_row_lit_and_it_still_works()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        window.UpdateLayout();

        ClickRailRow(window, "Modify an item's value");
        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));

        ClickRailRow(window, "New crafting recipe");
        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.True(LitRows(window) == 1, Describe(window, vm));

        // Go back to Items, then click the MAKE row again: it must still start that list.
        ClickRailRow(window, "Modify an item's value");
        ClickRailRow(window, "New crafting recipe");
        Assert.Equal(1, vm.Bands.Count(b => b.Selected is not null));
        Assert.True(LitRows(window) == 1, Describe(window, vm));
        Assert.Equal("Recipes", vm.Workspace.SelectedKind);
    }

    /// <summary>Lit rows in the rail only (the Workspace has list boxes of its own).</summary>
    private static int LitRows(MainWindow window) =>
        window.GetVisualDescendants().OfType<ListBoxItem>()
            .Count(i => i.IsSelected && i.FindAncestorOfType<ListBox>()?.DataContext is NavBandSection);

    private static string Describe(MainWindow window, ShellViewModel vm)
    {
        var lit = window.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.IsSelected)
            .Select(i =>
            {
                var owner = i.FindAncestorOfType<ListBox>();
                var ownerBand = owner?.DataContext as NavBandSection;
                var label = (i.DataContext as NavItem)?.Label
                            ?? $"<{i.DataContext?.GetType().Name ?? "null"}>";
                var parent = i.GetVisualAncestors().OfType<Control>()
                    .Select(a => a.Name is { Length: > 0 } n ? $"{a.GetType().Name}#{n}" : a.GetType().Name)
                    .Take(6);
                return ownerBand is not null
                    ? $"{ownerBand.Band}:{label}"
                    : $"?({string.Join(" < ", parent)}):{label}";
            })
            .ToList();
        var bands = string.Join(", ", vm.Bands.Select(b =>
            $"{b.Band}={(b.Selected?.Label ?? "none")}"));
        return $"lit rows = [{string.Join(" | ", lit)}]; bands = [{bands}]";
    }

    /// <summary>Clicks a rail row the way a mouse does (the ListBox decides, not the view model).</summary>
    private static void ClickRailRow(MainWindow window, string label)
    {
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == label && t.FindAncestorOfType<ListBox>() is not null);
        var item = text.FindAncestorOfType<ListBoxItem>()!;
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
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
        ship.Selected = ship.Items.Single(i => i.Key == ScreenKey.Mods);

        Assert.Equal(ScreenKey.Mods, nav.Current);
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
    public void The_app_opens_on_projects_with_its_rail_row_lit()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var first = window.GetVisualDescendants().OfType<ListBoxItem>().First();
        Assert.True(first.IsSelected);
        Assert.Equal("Projects", (first.DataContext as NavItem)?.Label);
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
        Assert.Contains(texts, t => t.Contains("Change item values") && t.Contains("Mod level 1"));
        Assert.Contains(texts, t => t.Contains("Create a new item") && t.Contains("Mod level 5"));
        Assert.Contains(texts, t => t.Contains("new menu section") && t.Contains("Mod level 4"));

        // The strip says only what ships today — no promises of later work (owner, 2026-11-07).
        Assert.DoesNotContain(texts, t => t.Contains("Coming up"));
        Assert.DoesNotContain(texts, t => t.Contains("moving parts"));
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
