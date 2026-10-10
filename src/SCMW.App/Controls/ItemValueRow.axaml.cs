using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// One value row: shows the value (clickable to edit), becomes a number box while
/// editing, and marks a changed value. Its DataContext is the ItemValue.
/// </summary>
public partial class ItemValueRow : UserControl
{
    public static readonly StyledProperty<ICommand?> BeginEditCommandProperty =
        AvaloniaProperty.Register<ItemValueRow, ICommand?>(nameof(BeginEditCommand));

    public static readonly StyledProperty<ICommand?> CommitEditCommandProperty =
        AvaloniaProperty.Register<ItemValueRow, ICommand?>(nameof(CommitEditCommand));

    public static readonly StyledProperty<ICommand?> CancelEditCommandProperty =
        AvaloniaProperty.Register<ItemValueRow, ICommand?>(nameof(CancelEditCommand));

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

    public ItemValueRow()
    {
        AvaloniaXamlLoader.Load(this);

        // When the editor appears, put the cursor in it so the player can type straight away.
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
        if (DataContext is ItemValue value) CommitEditCommand?.Execute(value);
    }

    private void Cancel()
    {
        if (DataContext is ItemValue value) CancelEditCommand?.Execute(value);
    }
}
