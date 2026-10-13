namespace SCMW.Core.Content;

/// <summary>
/// The rules for the names the app shows for an asset, kept in one place so the reader (which
/// indexes them) and the screen (which shows and records them) can never drift.
/// </summary>
public static class AssetNames
{
    private const string BaseElementPrefix = "BP_Base_";

    /// <summary>True when a raw asset name is a base-building element (a <c>BP_Base_*</c> blueprint).</summary>
    public static bool IsBaseElementName(string assetName) =>
        !string.IsNullOrEmpty(assetName)
        && assetName.StartsWith(BaseElementPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The friendly name for a base element: <c>BP_Base_Improvised_Wooden_Chest</c> →
    /// <c>Improvised Wooden Chest (base)</c>.
    /// <para>
    /// The <c>(base)</c> marker is deliberate: a placeable usually exists as **both** an item and a
    /// base element, and they would share a name otherwise. The build finds an asset by name, so two
    /// identical names are refused as ambiguous — the marker keeps them distinct.
    /// </para>
    /// </summary>
    public static string BaseElementFriendly(string assetName)
    {
        var name = IsBaseElementName(assetName)
            ? assetName[BaseElementPrefix.Length..]
            : assetName;
        name = name.Replace('_', ' ').Trim();
        return name.Length == 0 ? name : name + " (base)";
    }

    private const string RecipePrefix = "CR_";

    /// <summary>True when a raw asset name is a crafting recipe (a <c>CR_*</c> data asset).</summary>
    public static bool IsRecipeName(string assetName) =>
        !string.IsNullOrEmpty(assetName)
        && assetName.StartsWith(RecipePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The friendly name for a recipe: <c>CR_Wooden_Arrow_Feathers</c> → <c>Wooden Arrow Feathers</c>.
    /// </summary>
    public static string RecipeFriendly(string assetName)
    {
        var name = IsRecipeName(assetName) ? assetName[RecipePrefix.Length..] : assetName;
        return name.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// A recipe's group, taken from the folder under <c>Recipes/</c> (e.g. <c>Placeables</c>,
    /// <c>Items</c>); falls back to "Recipes" when the path has no such folder.
    /// </summary>
    public static string RecipeCategory(string technicalPath)
    {
        const string marker = "/Recipes/";
        var at = technicalPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return "Recipes";

        var rest = technicalPath[(at + marker.Length)..];
        var slash = rest.IndexOf('/');
        return slash > 0 ? rest[..slash] : "Recipes";
    }
}
