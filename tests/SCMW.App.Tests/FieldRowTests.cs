using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using SCMW.App.Controls;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The Workspace's field row: shows a field's value, lets the owner type a new one (no padlocks),
/// and marks a changed value. Its DataContext is the <see cref="WorkspaceField"/>.
/// </summary>
public class FieldRowTests
{
    private static WorkspaceField Field(string label = "Weight", string value = "10", bool writable = true,
                                        FieldType type = FieldType.Number) =>
        new(label, "grams", value, Risk.Low, writable, "How heavy it is.", label, described: true, type: type);

    private static FieldRow NewRow(WorkspaceField field) => new() { DataContext = field };

    [AvaloniaFact]
    public void Row_shows_the_value_with_a_clickable_value()
    {
        var row = NewRow(Field());
        row.Measure(new Size(400, 60));

        Assert.Equal("10", row.FindControl<TextBlock>("ValueText")!.Text);
        Assert.True(row.FindControl<Button>("ValueButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_switches_to_the_editor_when_editing()
    {
        var field = Field();
        var row = NewRow(field);
        row.Measure(new Size(400, 60));

        var editor = row.FindControl<TextBox>("Editor")!;
        Assert.False(editor.IsVisible);

        field.IsEditing = true;
        row.Measure(new Size(400, 60));

        Assert.True(editor.IsVisible);
        Assert.False(row.FindControl<Button>("ValueButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_marks_a_changed_value()
    {
        var field = Field();
        var row = NewRow(field);
        row.Measure(new Size(400, 60));

        Assert.False(row.FindControl<Border>("ChangedChip")!.IsVisible);

        field.Load("25", "10");
        row.Measure(new Size(400, 60));

        Assert.True(row.FindControl<Border>("ChangedChip")!.IsVisible);
        Assert.Equal("was 10", row.FindControl<TextBlock>("WasText")!.Text);
    }

    [AvaloniaFact]
    public void Row_shows_a_plain_value_for_a_field_we_cannot_write()
    {
        var row = NewRow(Field(writable: false));
        row.Measure(new Size(400, 60));

        Assert.False(row.FindControl<Button>("ValueButton")!.IsVisible);
        Assert.True(row.FindControl<TextBlock>("ReadOnlyText")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_shows_a_pencil_beside_the_label_for_a_field_we_can_write()
    {
        var row = NewRow(Field(writable: true));
        row.Measure(new Size(400, 60));

        var cell = row.FindControl<StackPanel>("LabelCell")!;
        Assert.Contains(cell.Children.OfType<TextBlock>(), t => t.Name == "EditIcon" && t.IsVisible);
        Assert.False(row.FindControl<Avalonia.Controls.Shapes.Path>("LockIcon")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_shows_a_padlock_beside_the_label_for_a_field_we_cannot_write()
    {
        var row = NewRow(Field(writable: false));
        row.Measure(new Size(400, 60));

        var cell = row.FindControl<StackPanel>("LabelCell")!;
        Assert.Contains(cell.Children.OfType<Avalonia.Controls.Shapes.Path>(),
                        p => p.Name == "LockIcon" && p.IsVisible);
        Assert.False(row.FindControl<TextBlock>("EditIcon")!.IsVisible);
    }

    private static (FieldRow row, WorkspaceField field) Wired()
    {
        var vm = new WorkspaceViewModel();
        var field = Field();
        var row = new FieldRow
        {
            DataContext = field,
            BeginEditCommand = vm.BeginEditCommand,
            CommitEditCommand = vm.CommitEditCommand,
            CancelEditCommand = vm.CancelEditCommand,
        };
        var window = new Window { Content = row, Width = 400, Height = 120 };
        window.Show();
        return (row, field);
    }

    [AvaloniaFact]
    public void Enter_commits_the_value()
    {
        var (row, field) = Wired();
        field.IsEditing = true;
        var editor = row.FindControl<TextBox>("Editor")!;
        editor.Text = "25";

        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        Assert.Equal("25", field.Value);
        Assert.False(field.IsEditing);
    }

    [AvaloniaFact]
    public void Escape_cancels_the_value()
    {
        var (row, field) = Wired();
        field.IsEditing = true;
        var editor = row.FindControl<TextBox>("Editor")!;
        editor.Text = "99";

        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

        Assert.Equal("10", field.Value);
        Assert.False(field.IsEditing);
    }

    [AvaloniaFact]
    public void Clicking_away_keeps_an_invalid_value_open()
    {
        var (row, field) = Wired();
        field.IsEditing = true;
        var editor = row.FindControl<TextBox>("Editor")!;
        editor.Text = "abc";

        editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

        Assert.True(field.IsEditing);
        Assert.Equal("10", field.Value);
    }

    [AvaloniaFact] // multiply was taken off the row: it had no real use here (kept for the bulk updater)
    public void Row_offers_no_multiply()
    {
        var row = NewRow(Field(type: FieldType.Number));
        row.Measure(new Size(400, 60));

        Assert.Null(row.FindControl<Button>("MultiplyButton"));
        Assert.Null(row.FindControl<TextBox>("FactorEditor"));
    }

    [AvaloniaFact] // a fraction is shown with three places after the dot, never float noise
    public void Row_shows_a_fraction_with_three_places()
    {
        var row = NewRow(Field(value: "0.15000000000000002"));
        row.Measure(new Size(400, 60));

        Assert.Equal("0.150", row.FindControl<TextBlock>("ValueText")!.Text);
    }
}
