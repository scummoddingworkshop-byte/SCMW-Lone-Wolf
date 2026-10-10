using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ItemsViewModelEditTests
{
    private static (ItemsViewModel vm, ItemSummary item) NewVmWithSelection()
    {
        var vm = new ItemsViewModel();
        vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");
        return (vm, vm.SelectedItem!);
    }

    [AvaloniaFact]
    public void Sections_show_only_changeable_values_by_default()
    {
        var (vm, item) = NewVmWithSelection();

        var values = vm.Sections.SelectMany(s => s.Values).ToList();
        Assert.All(values, v => Assert.True(v.Editable));
        Assert.Contains(vm.Sections, s => s.Name == "Inventory");

        vm.ShowAllValues = true;
        var all = vm.Sections.SelectMany(s => s.Values).ToList();
        Assert.Contains(all, v => !v.Editable);
    }

    [AvaloniaFact]
    public void Begin_edit_prefills_the_draft_and_closes_other_editors()
    {
        var (vm, item) = NewVmWithSelection();
        var stack = item.Values.First(v => v.Name == "Max stack");
        var weight = item.Values.First(v => v.Name == "Weight");

        vm.BeginEditCommand.Execute(weight);
        vm.BeginEditCommand.Execute(stack);

        Assert.False(weight.IsEditing);
        Assert.True(stack.IsEditing);
        Assert.Equal("10", stack.Draft);
    }

    [AvaloniaFact]
    public void Commit_saves_a_valid_value_and_marks_it_changed()
    {
        var (vm, item) = NewVmWithSelection();
        var stack = item.Values.First(v => v.Name == "Max stack");

        vm.BeginEditCommand.Execute(stack);
        stack.Draft = "25";
        vm.CommitEditCommand.Execute(stack);

        Assert.Equal("25", stack.Value);
        Assert.True(stack.IsChanged);
        Assert.False(stack.IsEditing);
    }

    [AvaloniaFact]
    public void Commit_keeps_the_editor_open_for_an_invalid_value()
    {
        var (vm, item) = NewVmWithSelection();
        var stack = item.Values.First(v => v.Name == "Max stack");

        vm.BeginEditCommand.Execute(stack);
        stack.Draft = "not a number";
        vm.CommitEditCommand.Execute(stack);

        Assert.Equal("10", stack.Value);
        Assert.True(stack.IsEditing);
    }

    [AvaloniaFact]
    public void Cancel_leaves_the_value_alone()
    {
        var (vm, item) = NewVmWithSelection();
        var stack = item.Values.First(v => v.Name == "Max stack");

        vm.BeginEditCommand.Execute(stack);
        stack.Draft = "999";
        vm.CancelEditCommand.Execute(stack);

        Assert.Equal("10", stack.Value);
        Assert.False(stack.IsEditing);
    }

    [AvaloniaFact]
    public void A_read_only_value_cannot_be_edited()
    {
        var (vm, item) = NewVmWithSelection();
        vm.ShowAllValues = true;
        var value = item.Values.First(v => !v.Editable);

        vm.BeginEditCommand.Execute(value);

        Assert.False(value.IsEditing);
    }

    [AvaloniaFact]
    public void Switching_items_closes_an_editor_left_open_with_an_invalid_draft()
    {
        var (vm, item) = NewVmWithSelection();
        var stack = item.Values.First(v => v.Name == "Max stack");

        vm.BeginEditCommand.Execute(stack);
        stack.Draft = "abc";

        vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Canned Beans");

        Assert.False(stack.IsEditing);
    }
}
