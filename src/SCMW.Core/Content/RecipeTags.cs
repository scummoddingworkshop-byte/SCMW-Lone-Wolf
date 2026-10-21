// src/SCMW.Core/Content/RecipeTags.cs
// The ingredient tags a recipe slot may accept. The game stores them as real assets named
// CI_* (under Items/Crafting/Ingredients/), so the picker is filled from the index, never a
// hand-kept list. Kept pure and UI-free: assets in, tags out.
namespace SCMW.Core.Content;

/// <summary>
/// One selectable ingredient tag: <paramref name="Name"/> is the raw value a recipe stores
/// (e.g. <c>CI_Metal_Scrap</c>); <paramref name="Label"/> is the plain words the owner reads;
/// <paramref name="Path"/> is the tag's own file when we know it (a project category lives in the
/// mod's folder, so its whole path must be written, not just its name). Null means "name only".
/// </summary>
public sealed record RecipeTag(string Name, string Label, string? Path = null) : IPickerItem;

/// <summary>The game's real ingredient tags, gathered from the indexed <c>CI_*</c> assets.</summary>
public static class RecipeTags
{
    /// <summary>The filename prefix every ingredient tag asset carries.</summary>
    private const string Prefix = "CI_";

    /// <summary>The tags in <paramref name="assets"/>, plain-label order, each name once. Never null.</summary>
    public static IReadOnlyList<RecipeTag> From(IReadOnlyList<AssetSummary>? assets)
    {
        if (assets is null || assets.Count == 0)
            return Array.Empty<RecipeTag>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<RecipeTag>();
        foreach (var asset in assets)
        {
            var name = NameOf(asset);
            if (name is null || !seen.Add(name))
                continue;

            tags.Add(new RecipeTag(name, Label(name)));
        }

        tags.Sort(static (a, b) =>
        {
            var byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return tags;
    }

    /// <summary>The raw tag name (an asset's file name) when the asset is a <c>CI_*</c> tag, else null.</summary>
    private static string? NameOf(AssetSummary asset)
    {
        if (asset is null || string.IsNullOrEmpty(asset.Id))
            return null;

        var slash = asset.Id.LastIndexOf('/');
        var name = slash >= 0 ? asset.Id[(slash + 1)..] : asset.Id;
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    /// <summary>The plain words for a tag name: <c>CI_Metal_Scrap</c> → <c>Metal Scrap</c>.</summary>
    public static string Label(string name)
    {
        var label = name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? name[Prefix.Length..]
            : name;
        label = label.Replace('_', ' ').Trim();
        return label.Length == 0 ? name : label;
    }
}
