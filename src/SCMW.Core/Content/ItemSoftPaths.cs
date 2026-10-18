namespace SCMW.Core.Content;

/// <summary>
/// The one place an item is turned into the game's own <b>soft object path</b> — the
/// <c>/Game/…</c> link the game stores inside an asset (a recipe's "Product", a dish's food). It is
/// kept pure and UI-free: an item id or package path in, a soft path out.
/// </summary>
public static class ItemSoftPaths
{
    private const string ContentRoot = "SCUM/Content";
    private const string GameRoot = "/Game";
    private const string ItemSuffix = "_ES";

    /// <summary>
    /// The soft path for an editable item file id: the game keeps the whole path in the link, so
    /// <c>SCUM/Content/…/Olives_ES</c> becomes <c>/Game/…/Olives.Olives_C</c> (drop the <c>_ES</c>,
    /// then <c>"{path}.{name}_C"</c>). Empty when the id is not an editable item.
    /// </summary>
    public static string FromItemFile(string? itemFileId)
    {
        if (string.IsNullOrEmpty(itemFileId)
            || !itemFileId.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var path = GameRoot + itemFileId[ContentRoot.Length..];
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        if (!name.EndsWith(ItemSuffix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var stem = name[..^ItemSuffix.Length];
        var folder = slash >= 0 ? path[..(slash + 1)] : string.Empty;
        return folder + stem + "." + stem + "_C";
    }

    /// <summary>
    /// The soft path for an item's own package path (a file the project created, no <c>_ES</c>):
    /// <c>SCUM/Content/…/OliveOil</c> becomes <c>/Game/…/OliveOil.OliveOil_C</c> (the item's generated
    /// class object). Empty when the path is outside the game folder or has no name.
    /// </summary>
    public static string FromItemPackage(string? itemPackagePath)
    {
        if (string.IsNullOrEmpty(itemPackagePath)
            || !itemPackagePath.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var path = GameRoot + itemPackagePath[ContentRoot.Length..];
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        if (name.EndsWith(ItemSuffix, StringComparison.OrdinalIgnoreCase)) name = name[..^ItemSuffix.Length];
        if (name.Length == 0) return string.Empty;

        var folder = slash >= 0 ? path[..(slash + 1)] : string.Empty;
        return folder + name + "." + name + "_C";
    }

    /// <summary>The plain words for a soft path: <c>…/Olive_Oil.Olive_Oil_C</c> → <c>Olive Oil</c>.</summary>
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
