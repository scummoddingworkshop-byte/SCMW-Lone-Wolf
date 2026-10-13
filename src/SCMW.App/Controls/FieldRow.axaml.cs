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

    public static readonly StyledProperty<ICommand?> BeginMultiplyCommandProperty =
        AvaloniaProperty.Register<FieldRow, ICommand?>(nameof(BeginMultiplyCommand));

    public static readonly StyledProperty<ICommand?> CommitMultiplyCommandProperty =
        AvaloniaProperty.Register<FieldRow, ICommand?>(nameof(CommitMultiplyCommand));

    public static readonly StyledProperty<ICommand?> CancelMultiplyCommandProperty =
        AvaloniaProperty.Register<FieldRow, ICommand?>(nameof(CancelMultiplyCommand));

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

    public ICommand? BeginMultiplyCommand
    {
        get => GetValue(BeginMultiplyCommandProperty);
        set => SetValue(BeginMultiplyCommandProperty, value);
    }

    public ICommand? CommitMultiplyCommand
    {
        get => GetValue(CommitMultiplyCommandProperty);
        set => SetValue(CommitMultiplyCommandProperty, value);
    }

    public ICommand? CancelMultiplyCommand
    {
        get => GetValue(CancelMultiplyCommandProperty);
        set => SetValue(CancelMultiplyCommandProperty, value);
    }

    public FieldRow()
    {
        AvaloniaXamlLoader.Load(this);

        // When an editor appears, put the cursor in it so the owner can type straight away.
        FocusWhenShown("Editor");
        FocusWhenShown("FactorEditor");
    }

    private void FocusWhenShown(string name)
    {
        if (this.FindControl<TextBox>(name) is not { } editor) return;

        editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && editor.IsVisible)
            {
                editor.Focus();
            }
        };
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

    private void OnFactorKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter applies the factor; Escape cancels. Clicking away also applies it.
        if (e.Key == Key.Enter)
        {
            CommitFactor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelFactor();
            e.Handled = true;
        }
    }

    private void OnFactorLostFocus(object? sender, RoutedEventArgs e) => CommitFactor();

    /// <summary>A quick choice ("×2", "half", "+50%"): fill the factor, then apply it at once.</summary>
    private void OnQuickFactor(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceField field) return;
        if (sender is Button { Tag: string factor })
        {
            field.FactorDraft = factor;
            CommitMultiplyCommand?.Execute(field);
        }
    }

    private void Commit()
    {
        if (DataContext is WorkspaceField field) CommitEditCommand?.Execute(field);
    }

    private void Cancel()
    {
        if (DataContext is WorkspaceField field) CancelEditCommand?.Execute(field);
    }

    private void CommitFactor()
    {
        if (DataContext is WorkspaceField field) CommitMultiplyCommand?.Execute(field);
    }

    private void CancelFactor()
    {
        if (DataContext is WorkspaceField field) CancelMultiplyCommand?.Execute(field);
    }
}
