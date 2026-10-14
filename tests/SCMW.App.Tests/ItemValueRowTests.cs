using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SCMW.App.Controls;
using SCMW.Core.Knowledge;

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

    [AvaloniaFact]
    public void Row_shows_a_one_line_note_under_a_noted_value()
    {
        var value = new ItemValue("Type", "Item settings", "EBaseElementType::Well",
            ItemValueKind.Text, editable: true, band: ValueBand.Noted, note: "A named choice.");
        var row = NewRow(value);
        row.Measure(new Size(400, 80));

        var note = row.FindControl<TextBlock>("NoteText")!;
        Assert.True(note.IsVisible);
        Assert.Equal("A named choice.", note.Text);
    }

    [AvaloniaFact]
    public void Row_shows_a_padlock_for_a_locked_value()
    {
        var value = new ItemValue("Tags", "Item settings", "(tags)", editable: false, band: ValueBand.Locked);
        var row = NewRow(value);
        row.Measure(new Size(400, 80));

        Assert.True(row.FindControl<Avalonia.Controls.Shapes.Path>("LockIcon")!.IsVisible);
        Assert.False(row.FindControl<Button>("ValueButton")!.IsVisible);
        Assert.Null(row.FindControl<TextBlock>("LockedText"));   // the repeated sentence is gone
    }

    [AvaloniaFact] // the pencil sits beside the label, matching the padlock (owner, 2026-10-14)
    public void Row_shows_the_pencil_beside_the_label_for_an_editable_value()
    {
        var row = NewRow(new ItemValue("Max stack", "Inventory", "10"));
        row.Measure(new Size(400, 60));

        var label = row.FindControl<StackPanel>("LabelCell")!;
        var pencils = row.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "✎").ToList();

        Assert.Single(pencils);   // one pencil, by the label — not a second one by the value
        Assert.Contains(pencils[0], label.GetLogicalDescendants());
    }

    [AvaloniaFact]
    public void Row_hides_the_pencil_for_a_locked_value()
    {
        var row = NewRow(new ItemValue("Tags", "Item settings", "(tags)",
            editable: false, band: ValueBand.Locked));
        row.Measure(new Size(400, 60));

        Assert.False(row.FindControl<TextBlock>("EditIcon")!.IsVisible);
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
