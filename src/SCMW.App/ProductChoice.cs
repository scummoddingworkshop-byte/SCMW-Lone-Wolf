using SCMW.Core;

namespace SCMW.App;

/// <summary>
/// One choice in the "Makes" dropdown when making a new recipe: the item the recipe produces.
/// <see cref="SoftPath"/> is the game's own soft object path (what the recipe stores and the build
/// writes); <see cref="Label"/> is the plain words the searchable dropdown shows and filters.
/// </summary>
public sealed record ProductChoice(string SoftPath, string Label) : IPickerItem;
