using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
    private WorkspaceViewModel? _vm;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => HookViewModel();
    }

    /// <summary>Follow the view model so the New-recipe panel's dropdown can be rebuilt per open.</summary>
    private void HookViewModel()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as WorkspaceViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ShowNewRecipe) && _vm?.ShowNewRecipe == true)
            BuildNewRecipePickers();
    }

    /// <summary>
    /// Builds fresh "start from" and "Makes" dropdowns for the newly opened panel. They are rebuilt
    /// rather than reused because the app's dropdown stops opening once its card has been hidden and
    /// shown again.
    /// </summary>
    private void BuildNewRecipePickers()
    {
        BuildPicker("NewRecipeDonorHost", "DonorPickerTemplate");
        if (_vm?.ShowProductPicker == true) BuildPicker("NewRecipeProductHost", "ProductPickerTemplate");
    }

    private void BuildPicker(string hostName, string templateKey)
    {
        if (_vm is null) return;
        if (this.FindControl<ContentControl>(hostName) is not { } host) return;
        if (Resources[templateKey] is not IDataTemplate template) return;

        host.Content = template.Build(_vm);
    }

    /// <summary>A recipe's tag picker moved: offer the new tag to the view model to record.</summary>
    private void OnTagChanged(object? sender, EventArgs e)
    {
        if (sender is Control { DataContext: IngredientSlotRow slot })
            (DataContext as WorkspaceViewModel)?.SetIngredientTagCommand.Execute(slot);
    }

    /// <summary>A dish's food picker moved: offer the new item to the view model to record.</summary>
    private void OnCookingFoodChanged(object? sender, EventArgs e)
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
