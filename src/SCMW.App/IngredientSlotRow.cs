using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SCMW.Core.Content;

namespace SCMW.App;

/// <summary>
/// One ingredient slot on a recipe page: the tag it accepts and the amount it needs at each skill
/// tier. The amounts are ordinary <see cref="WorkspaceField"/> rows (so they edit and record like
/// any other value); the tag is a picker over the game's real tags.
/// </summary>
public partial class IngredientSlotRow : ObservableObject
{
    public IngredientSlotRow(int index, RecipeTag? tag, string? originalTagName,
                             IReadOnlyList<RecipeTag> tagChoices, IReadOnlyList<WorkspaceField> amounts)
    {
        Index = index;
        OriginalTagName = originalTagName;
        TagChoices = tagChoices;
        Amounts = amounts;
        _selectedTag = tag;
    }

    /// <summary>The slot's position in the recipe's own <c>Ingredients</c> list.</summary>
    public int Index { get; }

    /// <summary>What the owner reads at the top of the row.</summary>
    public string Header => $"Ingredient {Index + 1}";

    /// <summary>The tag the file had before any change (the "was …" baseline for the picker).</summary>
    public string? OriginalTagName { get; }

    /// <summary>The game's real tags, for the picker.</summary>
    public IReadOnlyList<RecipeTag> TagChoices { get; }

    /// <summary>The slot's per-skill amounts, in skill order.</summary>
    public IReadOnlyList<WorkspaceField> Amounts { get; }

    /// <summary>The tag this slot currently accepts.</summary>
    [ObservableProperty] private RecipeTag? _selectedTag;
}
