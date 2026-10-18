using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.Core;

namespace SCMW.App.Tests;

/// <summary>
/// The app's one dropdown: it shows the chosen item, filters its list as the owner types, and tells
/// the screen only when the owner actually picks (never on a programmatic set).
/// </summary>
public class SearchPickerTests
{
    private sealed record Choice(string Label) : IPickerItem;

    private static Choice[] Choices() => new[] { new Choice("Apple"), new Choice("Banana"), new Choice("Cherry") };

    private static (SearchPicker Picker, Window Window, ListBox Options, TextBox Search) Shown(params Choice[] choices)
    {
        var picker = new SearchPicker { ItemsSource = choices };
        var window = new Window { Content = picker, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();

        var options = picker.GetLogicalDescendants().OfType<ListBox>().Single();
        var search = picker.GetLogicalDescendants().OfType<TextBox>().Single();
        return (picker, window, options, search);
    }

    private static int ShownCount(ListBox options) =>
        options.ItemsSource is System.Collections.IEnumerable items ? items.Cast<object>().Count() : -1;

    [AvaloniaFact]
    public void It_starts_showing_every_choice()
    {
        var (_, _, options, _) = Shown(Choices());

        Assert.Equal(3, ShownCount(options));
    }

    [AvaloniaFact]
    public void Typing_narrows_the_list()
    {
        var (_, window, options, search) = Shown(Choices());

        search.Text = "an";
        window.UpdateLayout();

        Assert.Equal(1, ShownCount(options));   // "Banana"
    }

    [AvaloniaFact]
    public void The_box_shows_the_chosen_item()
    {
        var (picker, _, _, _) = Shown(Choices());

        picker.SelectedItem = new Choice("Apple");

        Assert.Equal("Apple", picker.FindControl<TextBlock>("ValueText")!.Text);
    }

    [AvaloniaFact]
    public void A_programmatic_set_does_not_count_as_the_owner_picking()
    {
        var (picker, _, _, _) = Shown(Choices());
        var raised = 0;
        picker.SelectionChanged += (_, _) => raised++;

        picker.SelectedItem = new Choice("Apple");

        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public void Picking_a_choice_sets_the_item_and_tells_the_screen()
    {
        var (picker, _, options, _) = Shown(Choices());
        var raised = 0;
        picker.SelectionChanged += (_, _) => raised++;

        options.SelectedIndex = 1;   // the owner clicks "Banana"

        Assert.Equal(1, raised);
        Assert.Equal("Banana", ((IPickerItem)picker.SelectedItem!).Label);
    }

    [AvaloniaFact] // opening the list must keep working after the picker's parent is hidden and shown again
    public void The_list_opens_again_after_its_parent_is_hidden_and_shown()
    {
        var picker = new SearchPicker { ItemsSource = Choices() };
        var host = new Border { Child = picker, IsVisible = true };
        var window = new Window { Content = host, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();

        var pop = picker.FindControl<Popup>("Pop")!;
        var toggle = picker.FindControl<Button>("Toggle")!;
        var options = picker.GetLogicalDescendants().OfType<ListBox>().Single();

        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert.True(pop.IsOpen);
        options.SelectedIndex = 0;
        window.UpdateLayout();
        Assert.False(pop.IsOpen);

        host.IsVisible = false;
        window.UpdateLayout();
        host.IsVisible = true;
        window.UpdateLayout();
        System.Threading.Thread.Sleep(400);

        // Build a fresh picker, as a screen would when it rebuilds its card.
        var fresh = new SearchPicker { ItemsSource = Choices() };
        host.Child = fresh;
        window.UpdateLayout();
        var freshToggle = fresh.FindControl<Button>("Toggle")!;
        var freshPop = fresh.FindControl<Popup>("Pop")!;

        freshToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();

        Assert.True(freshPop.IsOpen);
    }
}
