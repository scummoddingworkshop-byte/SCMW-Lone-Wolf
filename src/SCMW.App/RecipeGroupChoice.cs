using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App;

/// <summary>
/// One choice in the "Crafting group" dropdown: a shelf of the game's crafting menu, its technical
/// tag (what the recipe stores and the build writes) and the plain words the owner reads.
/// </summary>
public sealed record RecipeGroupChoice(string Tag, string Label, RecipeGroup Group,
    bool IsProjectSection = false) : IPickerItem;
