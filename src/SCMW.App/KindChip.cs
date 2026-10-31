using System.Collections.Generic;

namespace SCMW.App;

/// <summary>
/// One chip on the Workspace's kind picker: the kind's known key (what the rest of the app uses, and
/// what a project's changes are recorded against) and the words the player reads.
/// </summary>
/// <remarks>
/// Both recipe families say "recipe" out loud: SCUM keeps crafting recipes and cooking dishes in two
/// different places, and a player should not have to work out which list a dish belongs to.
/// </remarks>
public sealed record KindChip(string Key, string Label)
{
    /// <summary>The chips, in the order they are shown.</summary>
    /// <remarks>
    /// The chips say what the player wants to do, not what the list is called: a player looking for a
    /// crafting recipe should not have to work out whether that lives under "Recipes" or "Cooking".
    /// </remarks>
    public static readonly IReadOnlyList<KindChip> All = new[]
    {
        new KindChip("Items", "Change a value"),
        new KindChip("Base building", "Change a base part"),
        new KindChip("Recipes", "Change a recipe"),
        new KindChip("Cooking", "Change a cooking dish"),
    };
}
