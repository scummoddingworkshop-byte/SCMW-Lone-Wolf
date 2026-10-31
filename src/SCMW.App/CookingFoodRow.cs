using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SCMW.Core.Content;

namespace SCMW.App;

/// <summary>
/// One food of a dish ingredient on the page: the item it points at (a picker over the game's real
/// foods) and the values it carries. The values are ordinary <see cref="WorkspaceField"/> rows (so
/// they edit and record like any other value); the item is a picker over the game's foods.
/// </summary>
public partial class CookingFoodRow : ObservableObject
{
    public CookingFoodRow(bool isMain, int ingredientIndex, int index, string? originalItemPath,
                          string? currentItemPath, bool hasItem, string? resourceLabel,
                          CookingFood? selected, IReadOnlyList<CookingFood> choices,
                          IReadOnlyList<WorkspaceField> values, bool canResetToResource = false,
                          bool ingredientIsResource = false)
    {
        IsMain = isMain;
        IngredientIndex = ingredientIndex;
        Index = index;
        OriginalItemPath = originalItemPath;
        _currentItemPath = currentItemPath;
        HasItem = hasItem;
        ResourceLabel = resourceLabel;
        Choices = choices;
        Values = values;
        _selectedFood = selected;
        CanResetToResource = canResetToResource;
        IngredientIsResource = ingredientIsResource;
    }

    /// <summary>Which of the dish's lists this food's ingredient lives in.</summary>
    public bool IsMain { get; }

    /// <summary>The food's ingredient's position in its own list.</summary>
    public int IngredientIndex { get; }

    /// <summary>The food's position in its ingredient's own <c>PossibleIngredient</c> list.</summary>
    public int Index { get; }

    /// <summary>What the owner reads at the top of the row.</summary>
    public string Header => $"Food {Index + 1}";

    /// <summary>The item's soft path before any change (the "was …" baseline for the picker).</summary>
    public string? OriginalItemPath { get; }

    /// <summary>True when the food points at a real item (a normal food, not a resource).</summary>
    public bool HasItem { get; }

    /// <summary>True when the food is a resource (milk/water): shown as a plain name, not a picker.</summary>
    public bool IsResource => !HasItem;

    /// <summary>The resource's plain name (e.g. <c>Milk</c>), or null.</summary>
    public string? ResourceLabel { get; }

    /// <summary>
    /// True when the file's own food is a resource and this row now points at an item, so the owner
    /// can put the resource back (the "↺ Oil" button).
    /// </summary>
    public bool CanResetToResource { get; }

    /// <summary>True when the file marks the whole ingredient as a resource, so switching one food
    /// over to an item must switch the ingredient too (the game keeps both in step).</summary>
    public bool IngredientIsResource { get; }

    /// <summary>The item's soft path with the project's change applied — what the picker shows.</summary>
    [ObservableProperty] private string? _currentItemPath;

    /// <summary>The game's real foods, for the picker.</summary>
    public IReadOnlyList<CookingFood> Choices { get; }

    /// <summary>The food's values (Liters, Usage, …), in the order the card shows them.</summary>
    public IReadOnlyList<WorkspaceField> Values { get; }

    /// <summary>The item this food currently points at.</summary>
    [ObservableProperty] private CookingFood? _selectedFood;
}
