// src/SCMW.Core/Content/CookingFoods.cs
// The foods a cooking dish's ingredient rows can point at. The game stores the choice as a soft
// object path (e.g. /Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C), while the app's index names
// the same item by its file (SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES). This file is the
// one place that turns one into the other — kept pure and UI-free: assets in, choices out.
namespace SCMW.Core.Content;

/// <summary>
/// One selectable food: <paramref name="SoftPath"/> is the game's own soft object path (what a dish
/// stores and what the build writes); <paramref name="Label"/> is the plain words the owner reads.
/// </summary>
public sealed record CookingFood(string SoftPath, string Label);

/// <summary>The game's real foods, gathered from the indexed items under the food folder.</summary>
public static class CookingFoods
{
    /// <summary>The item file prefix, and the game folder the cookable foods live under.</summary>
    private const string ItemSuffix = "_ES";
    private const string ContentRoot = "SCUM/Content";
    private const string GameRoot = "/Game";
    private const string FoodRoot = "SCUM/Content/ConZ_Files/Items/Food/";

    /// <summary>The foods in <paramref name="assets"/>, plain-label order, each once. Never null.</summary>
    public static IReadOnlyList<CookingFood> From(IReadOnlyList<AssetSummary>? assets)
    {
        if (assets is null || assets.Count == 0)
            return Array.Empty<CookingFood>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foods = new List<CookingFood>();
        foreach (var asset in assets)
        {
            var id = asset?.Id;
            if (string.IsNullOrEmpty(id) || !id.StartsWith(FoodRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            var soft = SoftPath(id);
            if (soft.Length == 0 || !seen.Add(soft))
                continue;

            foods.Add(new CookingFood(soft, Label(soft)));
        }

        foods.Sort(static (a, b) =>
        {
            var byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.Compare(a.SoftPath, b.SoftPath, StringComparison.OrdinalIgnoreCase);
        });

        return foods;
    }

    /// <summary>
    /// The game's soft object path for an item id, or empty when the id is not an editable item.
    /// The game keeps the whole path in the soft link, so <c>SCUM/Content/…/Apple_ES</c> becomes
    /// <c>/Game/…/Apple.Apple_C</c> (drop the <c>_ES</c>, then <c>"{path}.{name}_C"</c>).
    /// </summary>
    public static string SoftPath(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId) || !itemId.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var path = GameRoot + itemId[ContentRoot.Length..];
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        if (!name.EndsWith(ItemSuffix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var stem = name[..^ItemSuffix.Length];
        var folder = slash >= 0 ? path[..(slash + 1)] : string.Empty;
        return folder + stem + "." + stem + "_C";
    }

    /// <summary>The plain words for a soft path: <c>…/Apple.Apple_C</c> → <c>Apple</c>.</summary>
    public static string Label(string? softPath)
    {
        if (string.IsNullOrEmpty(softPath)) return string.Empty;

        var slash = softPath.LastIndexOf('/');
        var last = slash >= 0 ? softPath[(slash + 1)..] : softPath;
        var dot = last.LastIndexOf('.');
        var name = dot >= 0 ? last[..dot] : last;
        name = name.Replace('_', ' ').Trim();
        return name.Length == 0 ? last : name;
    }
}
