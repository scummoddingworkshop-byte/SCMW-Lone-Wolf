using System;
using System.ComponentModel;
using Avalonia;
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
        HookDivider();
    }

    /// <summary>
    /// Starts the list at the width the player last chose, and records any drag of the divider so
    /// the next launch opens the same way.
    /// </summary>
    private void HookDivider()
    {
        if (this.FindControl<Grid>("WorkspaceColumns") is not { } columns) return;
        if (columns.ColumnDefinitions.Count == 0 || columns.ColumnDefinitions[0] is not { } list) return;

        list.Width = new GridLength(WorkspaceLayout.SavedWidth);
        SCMW.Core.Logging.Log.Info($"Workspace list column set to {list.Width.Value}");

        if (this.FindControl<GridSplitter>("ListSplitter") is { } splitter)
            splitter.DragCompleted += (_, _) => WorkspaceLayout.Remember(list.ActualWidth);
    }

    /// <summary>The width the list column is really using right now (the saved one before any layout).</summary>
    public double ListWidth =>
        this.FindControl<Grid>("WorkspaceColumns") is { ColumnDefinitions.Count: > 0 } columns
            && columns.ColumnDefinitions[0].ActualWidth > 0
            ? columns.ColumnDefinitions[0].ActualWidth
            : WorkspaceLayout.SavedWidth;

    /// <summary>
    /// Clicking a file in the list means "work on this one", so the New panel steps aside. This runs
    /// on every click, including one on the row that is already selected (which changes no property
    /// and so would otherwise leave the panel covering the file).
    /// </summary>
    private void OnAssetListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (DataContext is WorkspaceViewModel { ShowNewRecipe: true } vm)
            vm.CancelNewRecipeCommand.Execute(null);
    }

    /// <summary>Follow the view model so the New-recipe panel's dropdown can be rebuilt per open.</summary>
    private void HookViewModel()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as WorkspaceViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;

            // A recipe may already be open when this screen is shown (e.g. after a screen change).
            if (_vm.ShowOpenMakes) BuildPicker("OpenMakesHost", "OpenProductPickerTemplate");

            // The category card's dropdowns are rebuilt per item open (same dropdown quirk).
            if (_vm.ShowItemCategories) BuildItemCategoryPickers();
            if (_vm.ShowItemIdentity) BuildPicker("PictureSourceHost", "PictureSourcePickerTemplate");

            // The recipe's shelf card, likewise rebuilt per recipe open.
            if (_vm.ShowCraftingGroup) BuildCraftingGroupPickers();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ShowNewRecipe) && _vm?.ShowNewRecipe == true)
            BuildNewRecipePickers();
        else if (e.PropertyName == nameof(WorkspaceViewModel.ShowOpenMakes) && _vm?.ShowOpenMakes == true)
            BuildPicker("OpenMakesHost", "OpenProductPickerTemplate");
        else if (e.PropertyName == nameof(WorkspaceViewModel.ShowItemCategories)
                 && _vm?.ShowItemCategories == true)
            BuildItemCategoryPickers();
        else if (e.PropertyName == nameof(WorkspaceViewModel.ShowNewCategoryForm)
                 && _vm?.ShowNewCategoryForm == true)
            BuildPicker("CategoryDonorPickerHost", "CategoryDonorPickerTemplate");
        else if (e.PropertyName == nameof(WorkspaceViewModel.ShowItemIdentity)
                 && _vm?.ShowItemIdentity == true)
            BuildPicker("PictureSourceHost", "PictureSourcePickerTemplate");
        else if (e.PropertyName == nameof(WorkspaceViewModel.ShowCraftingGroup)
                 && _vm?.ShowCraftingGroup == true)
            BuildCraftingGroupPickers();

        // Picking a shelf can reveal the Place dropdown later; rebuild it as well, since a picker
        // that has been hidden and shown again is exactly the case that stops reopening.
        else if (e.PropertyName == nameof(WorkspaceViewModel.ShowCraftingPlace)
                 && _vm?.ShowCraftingPlace == true)
            BuildPicker("CraftingPlaceHost", "CraftingPlacePickerTemplate");
    }

    /// <summary>Builds fresh shelf and place dropdowns for the open recipe's card.</summary>
    private void BuildCraftingGroupPickers()
    {
        BuildPicker("CraftingGroupHost", "CraftingGroupPickerTemplate");
        BuildPicker("CraftingPlaceHost", "CraftingPlacePickerTemplate");
    }

    /// <summary>Builds fresh "add a category" and "copy the shape from" dropdowns for the card.</summary>
    private void BuildItemCategoryPickers()
    {
        BuildPicker("ItemCategoryPickerHost", "ItemCategoryPickerTemplate");
        BuildPicker("CategoryDonorPickerHost", "CategoryDonorPickerTemplate");
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

    /// <summary>Leaving a name/description box saves the words, so they are never left behind.</summary>
    private void OnItemIdentityLostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as WorkspaceViewModel)?.SaveItemIdentityCommand.Execute(null);

    /// <summary>Enter in the name box saves it; the description box keeps Enter for new lines.</summary>
    private void OnItemIdentityKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        (DataContext as WorkspaceViewModel)?.SaveItemIdentityCommand.Execute(null);
        e.Handled = true;
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
