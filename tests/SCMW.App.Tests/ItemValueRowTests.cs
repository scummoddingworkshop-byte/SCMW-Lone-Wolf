using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class ItemValueRowTests
{
    private static ItemValueRow NewRow(ItemValue value) => new() { DataContext = value };

    [AvaloniaFact]
    public void Row_shows_the_name_and_value_with_a_clickable_value()
    {
        var value = new ItemValue("Max stack", "Inventory", "10");
        var row = NewRow(value);
        row.Measure(new Size(400, 60));

        var texts = row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Max stack", texts);
        Assert.Contains("10", texts);
        Assert.True(row.FindControl<Button>("ValueButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_switches_to_the_editor_when_editing()
    {
        var value = new ItemValue("Max stack", "Inventory", "10");
        var row = NewRow(value);
        row.Measure(new Size(400, 60));

        var editor = row.FindControl<TextBox>("Editor")!;
        Assert.False(editor.IsVisible);

        value.IsEditing = true;
        row.Measure(new Size(400, 60));

        Assert.True(editor.IsVisible);
        Assert.False(row.FindControl<Button>("ValueButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_marks_a_changed_value()
    {
        var value = new ItemValue("Max stack", "Inventory", "10");
        var row = NewRow(value);
        row.Measure(new Size(400, 60));

        Assert.False(row.FindControl<Border>("ChangedChip")!.IsVisible);

        value.Value = "25";
        row.Measure(new Size(400, 60));

        Assert.True(row.FindControl<Border>("ChangedChip")!.IsVisible);
        Assert.Equal("was 10", row.FindControl<TextBlock>("WasText")!.Text);
    }

    [AvaloniaFact]
    public void Row_shows_a_plain_value_for_a_read_only_one()
    {
        var value = new ItemValue("Value", "Economy", "5", editable: false);
        var row = NewRow(value);
        row.Measure(new Size(400, 60));

        Assert.False(row.FindControl<Button>("ValueButton")!.IsVisible);
        Assert.True(row.FindControl<TextBlock>("ReadOnlyText")!.IsVisible);
    }

    private static (ItemValueRow row, ItemValue value) Wired()
    {
        var vm = new ItemsViewModel();
        var value = new ItemValue("Max stack", "Inventory", "10");
        var row = new ItemValueRow
        {
            DataContext = value,
            BeginEditCommand = vm.BeginEditCommand,
            CommitEditCommand = vm.CommitEditCommand,
            CancelEditCommand = vm.CancelEditCommand,
        };
        var window = new Window { Content = row, Width = 400, Height = 120 };
        window.Show();
        return (row, value);
    }

    [AvaloniaFact]
    public void Opening_the_editor_focuses_it()
    {
        var (row, value) = Wired();

        value.IsEditing = true;

        Assert.True(row.FindControl<TextBox>("Editor")!.IsFocused);
    }

    [AvaloniaFact]
    public void Enter_commits_the_value()
    {
        var (row, value) = Wired();
        value.IsEditing = true;
        var editor = row.FindControl<TextBox>("Editor")!;
        editor.Text = "25";

        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        Assert.Equal("25", value.Value);
        Assert.False(value.IsEditing);
    }

    [AvaloniaFact]
    public void Escape_cancels_the_value()
    {
        var (row, value) = Wired();
        value.IsEditing = true;
        var editor = row.FindControl<TextBox>("Editor")!;
        editor.Text = "99";

        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

        Assert.Equal("10", value.Value);
        Assert.False(value.IsEditing);
    }

    [AvaloniaFact]
    public void Clicking_away_keeps_an_invalid_value_open()
    {
        var (row, value) = Wired();
        value.IsEditing = true;
        var editor = row.FindControl<TextBox>("Editor")!;
        editor.Text = "abc";

        editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

        Assert.True(value.IsEditing);
        Assert.Equal("10", value.Value);
    }
}
