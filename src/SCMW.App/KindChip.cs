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
    public static readonly IReadOnlyList<KindChip> All = new[]
    {
        new KindChip("Items", "Items"),
        new KindChip("Base building", "Base building"),
        new KindChip("Recipes", "Items Recipes"),
        new KindChip("Cooking", "Cooking Recipes"),
    };
}
