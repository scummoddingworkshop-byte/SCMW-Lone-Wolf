using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>
/// One starting button on the Workspace: the kind's known key (what the rest of the app uses, and what
/// a project's changes are recorded against) and the words the player reads.
/// </summary>
/// <remarks>
/// The buttons say what the player wants to do, not what the list is called: someone looking for a
/// crafting recipe should not have to work out whether that lives under "Recipes" or "Cooking". The one
/// they are browsing says so by being lit up.
/// </remarks>
public sealed partial class KindChip : ObservableObject
{
    public KindChip(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>The kind's key, as the rest of the app knows it.</summary>
    public string Key { get; }

    /// <summary>What the player reads.</summary>
    public string Label { get; }

    /// <summary>True for the list being browsed now (that button is lit up).</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>The buttons, in the order they are shown.</summary>
    public static readonly IReadOnlyList<KindChip> All = new[]
    {
        new KindChip("Items", "Change a value"),
        new KindChip("Base building", "Change a base part"),
        new KindChip("Recipes", "Change a recipe"),
        new KindChip("Cooking", "Change a cooking dish"),
    };
}
