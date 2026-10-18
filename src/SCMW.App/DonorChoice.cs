using SCMW.Core;

namespace SCMW.App;

/// <summary>
/// One choice in the "start from" dropdown when making a new recipe: the recipe the owner picks to
/// copy. It is any recipe the app can show, keyed by its exact game path so two same-named recipes
/// can never be mixed up. Its plain <see cref="Label"/> is what the searchable dropdown shows and filters.
/// </summary>
public sealed record DonorChoice(string Name, string Path) : IPickerItem
{
    /// <summary>The plain words the owner reads and types against (the recipe's name).</summary>
    public string Label => Name;
}
