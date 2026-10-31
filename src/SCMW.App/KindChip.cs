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
    public KindChip(string key, string label, string blurb)
    {
        Key = key;
        Label = label;
        Blurb = blurb;
    }

    /// <summary>The kind's key, as the rest of the app knows it.</summary>
    public string Key { get; }

    /// <summary>What the player reads.</summary>
    public string Label { get; }

    /// <summary>A plain line under the buttons: what can be done in this list.</summary>
    public string Blurb { get; }

    /// <summary>True for the list being browsed now (that button is lit up).</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>The buttons, in the order they are shown.</summary>
    public static readonly IReadOnlyList<KindChip> All = new[]
    {
        new KindChip("Items", "Modify an item's value", "You can modify an item's value here"),
        new KindChip("Base building", "Modify a base part", "You can modify a base part here"),
        new KindChip("Recipes", "Modify a crafting recipe", "You can modify a crafting recipe here"),
        new KindChip("Cooking", "Modify a cooking recipe", "You can modify a cooking recipe here"),
    };
}
