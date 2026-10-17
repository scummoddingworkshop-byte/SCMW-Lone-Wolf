using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Workspace screen: shows the real fields of whichever file is open, each explained
/// (label, unit, risk dot, plain meaning). Reads everything from its view model.
/// </summary>
public partial class WorkspaceView : UserControl
{
    public WorkspaceView() => AvaloniaXamlLoader.Load(this);

    /// <summary>A recipe's tag picker moved: offer the new tag to the view model to record.</summary>
    private void OnTagChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is Control { DataContext: IngredientSlotRow slot })
            (DataContext as WorkspaceViewModel)?.SetIngredientTagCommand.Execute(slot);
    }

    /// <summary>A dish's food picker moved: offer the new item to the view model to record.</summary>
    private void OnCookingFoodChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is Control { DataContext: CookingFoodRow food })
            (DataContext as WorkspaceViewModel)?.SetCookingFoodCommand.Execute(food);
    }

    /// <summary>Enter applies an ingredient amount and Escape cancels, matching the field rows.</summary>
    private void OnAmountKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: WorkspaceField field }) return;

        if (e.Key == Key.Enter)
        {
            CommitAmount(field);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelAmount(field);
            e.Handled = true;
        }
    }

    private void OnAmountLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: WorkspaceField field })
            CommitAmount(field);
    }

    private void CommitAmount(WorkspaceField field) =>
        (DataContext as WorkspaceViewModel)?.CommitEditCommand.Execute(field);

    private void CancelAmount(WorkspaceField field) =>
        (DataContext as WorkspaceViewModel)?.CancelEditCommand.Execute(field);
}
