using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class ItemsViewTests
{
    private static ItemsView ShownView(IReadOnlyList<ItemSummary>? items = null)
    {
        var view = new ItemsView { DataContext = new ItemsViewModel(items: items) };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void Items_shows_both_columns_and_the_controls()
    {
        var view = ShownView();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.NotNull(view.FindControl<TextBox>("SearchBox"));
        Assert.NotNull(view.FindControl<ListBox>("CategoryChips"));
        Assert.NotNull(view.FindControl<ListBox>("ItemList"));
        Assert.Contains("Duct Tape", texts);
        Assert.True(view.FindControl<Border>("Hint")!.IsVisible);       // nothing selected yet
    }

    [AvaloniaFact]
    public void Selecting_an_item_shows_its_values()
    {
        var vm = new ItemsViewModel();
        vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");

        var view = new ItemsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();

        Assert.False(view.FindControl<Border>("Hint")!.IsVisible);
        Assert.True(view.FindControl<Border>("Detail")!.IsVisible);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("INVENTORY", texts);
        Assert.Contains("Max stack", texts);
    }

    [AvaloniaFact]
    public void Items_empty_and_no_matches_states_are_exclusive()
    {
        var empty = ShownView(Array.Empty<ItemSummary>());
        Assert.True(empty.FindControl<Border>("EmptyState")!.IsVisible);
        Assert.False(empty.FindControl<Border>("NoMatches")!.IsVisible);

        var view = ShownView();
        ((ItemsViewModel)view.DataContext!).Search = "zzz-no-match";
        Assert.True(view.FindControl<Border>("NoMatches")!.IsVisible);
        Assert.False(view.FindControl<Border>("EmptyState")!.IsVisible);
    }

    [AvaloniaFact]
    public void Items_value_rows_are_wired_to_the_edit_commands()
    {
        var vm = new ItemsViewModel();
        vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");

        var view = new ItemsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<ItemValueRow>().First();
        Assert.Same(vm.BeginEditCommand, row.BeginEditCommand);
        Assert.Same(vm.CommitEditCommand, row.CommitEditCommand);
        Assert.Same(vm.CancelEditCommand, row.CancelEditCommand);
    }

    [AvaloniaFact]
    public void Items_area_shows_in_the_shell()
    {
        var vm = new ShellViewModel(new NavigationHost());
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Items);

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.IsType<ItemsView>(vm.ActiveView);
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Duct Tape", texts);
    }

    [AvaloniaFact]
    public void A_selected_item_row_uses_dark_text_on_the_accent()
    {
        var vm = new ItemsViewModel();
        var view = new ItemsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();

        var list = view.FindControl<ListBox>("ItemList")!;
        list.SelectedIndex = 0;

        var container = Assert.IsAssignableFrom<ListBoxItem>(list.ContainerFromIndex(0));
        var name = container.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == vm.VisibleItems[0].Name);

        var brush = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(name.Foreground);
        Assert.Equal(Avalonia.Media.Color.Parse("#12100E"), brush.Color);   // OnAccentBrush
    }
}
