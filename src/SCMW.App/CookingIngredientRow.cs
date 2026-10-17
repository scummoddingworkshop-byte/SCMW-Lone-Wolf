using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>
/// One ingredient of a dish on the page: whether it is main or optional, its title, and the foods it
/// may take. The title is an ordinary <see cref="WorkspaceField"/> row (so it edits and records like
/// any other value); the foods are their own rows.
/// </summary>
public partial class CookingIngredientRow : ObservableObject
{
    public CookingIngredientRow(bool isMain, int index, WorkspaceField? title,
                                IReadOnlyList<CookingFoodRow> foods)
    {
        IsMain = isMain;
        Index = index;
        Title = title;
        Foods = foods;
    }

    /// <summary>True for a main ingredient, false for an optional one.</summary>
    public bool IsMain { get; }

    /// <summary>The ingredient's position in its own list (main or optional).</summary>
    public int Index { get; }

    /// <summary>What the owner reads at the top of the row.</summary>
    public string Header => (IsMain ? "Main ingredient " : "Optional ingredient ") + (Index + 1);

    /// <summary>The ingredient's title row, or null when the file has no title.</summary>
    public WorkspaceField? Title { get; }

    /// <summary>True when the file carries a title for this ingredient.</summary>
    public bool HasTitle => Title is not null;

    /// <summary>The foods this ingredient may take, in file order.</summary>
    public IReadOnlyList<CookingFoodRow> Foods { get; }
}
