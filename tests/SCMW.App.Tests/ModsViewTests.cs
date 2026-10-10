using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class ModsViewTests
{
    private static (ModsView view, ModsViewModel vm) Shown(ModList? store = null)
    {
        var vm = new ModsViewModel(store ?? new ModList());
        var view = new ModsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();
        return (view, vm);
    }

    [AvaloniaFact]
    public void The_restart_as_administrator_button_follows_the_flag()
    {
        var (view, vm) = Shown();

        var button = view.FindControl<Button>("RestartAdminButton");
        Assert.NotNull(button);
        Assert.False(button!.IsVisible);

        vm.CanRestartAsAdmin = true;

        Assert.True(button.IsVisible);
        Assert.Equal("Restart as administrator", button.Content);
    }

    [AvaloniaFact]
    public void Mods_shows_the_title_summary_and_drop_zone()
    {
        var store = new ModList();
        store.Install("My first mod");
        store.Add("SomeMod.pak");
        var (view, _) = Shown(store);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Mods", texts);
        Assert.Contains("2 mods installed · 2 switched on.", texts);
        Assert.True(view.FindControl<Border>("DropZone")!.IsVisible);
        Assert.NotNull(view.FindControl<Button>("AddModButton"));
        Assert.NotNull(view.FindControl<TextBlock>("MessageText"));
    }

    [AvaloniaFact]
    public void Mods_lists_each_mod()
    {
        var store = new ModList();
        store.Install("My first mod");
        store.Add("SomeMod.pak");
        var (view, _) = Shown(store);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("My first mod", texts);
        Assert.Contains("SomeMod.pak", texts);
        Assert.Contains("SCMW", texts);
        Assert.Contains("External", texts);
    }

    [AvaloniaFact]
    public void Mods_empty_state_explains_both_ways_to_add_one()
    {
        var (view, _) = Shown();

        Assert.True(view.FindControl<Border>("EmptyState")!.IsVisible);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("No mods installed yet", texts);
    }

    [AvaloniaFact]
    public void Mods_rows_are_wired_to_the_view_model_commands()
    {
        var store = new ModList();
        var mod = store.Add("SomeMod.pak");
        var vm = new ModsViewModel(store);
        var view = new ModsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<ModRow>().First();
        Assert.Same(vm.RequestRemoveCommand, row.RequestRemoveCommand);
        Assert.Same(vm.ConfirmRemoveCommand, row.ConfirmRemoveCommand);
        Assert.Same(vm.CancelRemoveCommand, row.CancelRemoveCommand);

        // The Remove button's resolved command starts the confirmation...
        var removeButton = row.FindControl<Button>("RemoveButton")!;
        Assert.NotNull(removeButton.Command);
        removeButton.Command!.Execute(removeButton.CommandParameter);
        Assert.True(mod.IsConfirmingRemove);

        // ...and Cancel genuinely cancels.
        var cancelButton = row.FindControl<Button>("CancelRemoveButton")!;
        Assert.NotNull(cancelButton.Command);
        cancelButton.Command!.Execute(cancelButton.CommandParameter);
        Assert.False(mod.IsConfirmingRemove);
    }
}
