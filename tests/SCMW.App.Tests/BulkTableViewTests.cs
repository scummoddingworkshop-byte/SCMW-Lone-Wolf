using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

/// <summary>
/// The bulk table screen (Task 5.1): a tick-box per file, the library's settable values only,
/// and an Apply that is wired to the view model.
/// </summary>
public class BulkTableViewTests
{
    private static BulkTableView Shown(out BulkTableViewModel vm)
    {
        vm = new BulkTableViewModel();
        vm.Load("Items", new[]
        {
            new ItemSummary("Tomato", "Food", "", editable: true, assetId: "/items/Food/Tomato_ES"),
            new ItemSummary("Fire Axe", "Weapons", "", editable: true, assetId: "/items/Weapons/Fire_Axe_ES"),
        });
        var view = new BulkTableView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void It_shows_a_tick_box_for_each_file()
    {
        var view = Shown(out _);

        var ticks = view.GetVisualDescendants().OfType<CheckBox>()
                        .Where(c => c.DataContext is BulkRow).ToList();

        Assert.Equal(2, ticks.Count);
    }

    [AvaloniaFact]
    public void Ticking_a_file_ticks_the_view_model_row()
    {
        var view = Shown(out var vm);

        var tick = view.GetVisualDescendants().OfType<CheckBox>()
                       .Single(c => c.DataContext is BulkRow r && r.Name == "Tomato");
        tick.IsChecked = true;

        Assert.True(vm.Rows.Single(r => r.Name == "Tomato").IsSelected);
    }

    [AvaloniaFact]
    public void It_offers_only_the_librarys_settable_values()
    {
        var view = Shown(out var vm);

        var picker = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "FieldPicker");

        Assert.Same(vm.Fields, picker.ItemsSource);
        Assert.Contains(vm.Fields, f => f.Name == "MaxQuantity");   // a described, writable value
        Assert.DoesNotContain(vm.Fields, f => f.Name == "Caption"); // a string-table key is never offered
    }

    [AvaloniaFact]
    public void The_apply_button_is_wired_to_the_view_model()
    {
        var view = Shown(out var vm);

        var apply = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyButton");

        Assert.Same(vm.ApplyCommand, apply.Command);
    }
}
