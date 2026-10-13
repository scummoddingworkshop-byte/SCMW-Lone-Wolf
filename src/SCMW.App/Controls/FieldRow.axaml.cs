using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// One field row of the Workspace. Shows the field's value, lets the owner type a new one (no
/// padlocks), and marks a changed value. Its DataContext is the <see cref="WorkspaceField"/>.
/// </summary>
public partial class FieldRow : UserControl
{
    public static readonly StyledProperty<ICommand?> BeginEditCommandProperty =
        AvaloniaProperty.Register<FieldRow, ICommand?>(nameof(BeginEditCommand));

    public static readonly StyledProperty<ICommand?> CommitEditCommandProperty =
        AvaloniaProperty.Register<FieldRow, ICommand?>(nameof(CommitEditCommand));

    public static readonly StyledProperty<ICommand?> CancelEditCommandProperty =
        AvaloniaProperty.Register<FieldRow, ICommand?>(nameof(CancelEditCommand));

    public ICommand? BeginEditCommand
    {
        get => GetValue(BeginEditCommandProperty);
        set => SetValue(BeginEditCommandProperty, value);
    }

    public ICommand? CommitEditCommand
    {
        get => GetValue(CommitEditCommandProperty);
        set => SetValue(CommitEditCommandProperty, value);
    }

    public ICommand? CancelEditCommand
    {
        get => GetValue(CancelEditCommandProperty);
        set => SetValue(CancelEditCommandProperty, value);
    }

    public FieldRow()
    {
        AvaloniaXamlLoader.Load(this);

        // When the editor appears, put the cursor in it so the owner can type straight away.
        if (this.FindControl<TextBox>("Editor") is { } editor)
        {
            editor.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && editor.IsVisible)
                {
                    editor.Focus();
                }
            };
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter applies the value; Escape cancels. Clicking away also applies it.
        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        if (DataContext is WorkspaceField field) CommitEditCommand?.Execute(field);
    }

    private void Cancel()
    {
        if (DataContext is WorkspaceField field) CancelEditCommand?.Execute(field);
    }
}
