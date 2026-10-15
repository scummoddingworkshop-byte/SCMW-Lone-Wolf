using System;
using System.Collections.Generic;
using System.Linq;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Knowledge;

namespace SCMW.App;

/// <summary>Which family of game objects a browser screen shows.</summary>
public enum AssetFamily { Items, BaseElements, Recipes, LootAndSpawn }

/// <summary>
/// Turns the engine's real content shapes into the Items screen's shapes. Labels come from the
/// engine's own presentation layers (FieldNames/FieldKnowledge); this class invents nothing.
///
/// Classification: a real SCUM asset name tells us what it is — an item's editable copy ends in
/// <c>_ES</c>; recipes (<c>CR_</c>), ingredients (<c>CI_</c>), icons (<c>ICO_</c>) and chopping
/// entries (<c>Chop_</c>) are not player items and are left out of the list for now (see
/// <c>Ideas/New Feature Wishlist.md</c>). Only an <c>_ES</c> item's fields are shown as
/// changeable, matching how the game and the proven earlier attempts edit items.
/// </summary>
public sealed class ContentCatalog
{
    private readonly IContentSource _source;
    private readonly IIconCache? _icons;

    public ContentCatalog(IContentSource source) : this(source, null) { }

    public ContentCatalog(IContentSource source, IIconCache? icons)
    {
        _source = source;
        _icons = icons;
    }

    /// <summary>Non-item filename prefixes: recipes, ingredients, icons, chopping entries.</summary>
    private static readonly string[] NonItemPrefixes = { "CR_", "CI_", "ICO_", "Chop_" };

    /// <summary>True when the asset name ends in <c>_ES</c> — the editable copy of a real item.</summary>
    internal static bool IsEditableItem(string assetId)
    {
        var name = AssetName(assetId);
        return name.EndsWith("_ES", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the asset is a base-building element (a <c>BP_Base_*</c> blueprint).</summary>
    internal static bool IsBaseElement(string assetId) =>
        AssetNames.IsBaseElementName(AssetName(assetId));

    /// <summary>True when the asset is a crafting recipe (a <c>CR_*</c> data asset).</summary>
    internal static bool IsRecipe(string assetId) =>
        AssetNames.IsRecipeName(AssetName(assetId));

    /// <summary>The loot &amp; spawn folders the reader indexes as their own family (Task 2.6).</summary>
    private static readonly string[] SpawnerRoots =
    {
        "SCUM/Content/ConZ_Files/Items/SpawnerPresets/",
        "SCUM/Content/ConZ_Files/Items/SpawnerPresets2/",
        "SCUM/Content/ConZ_Files/Characters/SpawnerPresets/",
    };

    /// <summary>True when the asset is a loot/spawn preset (a package under one of the spawner roots).</summary>
    internal static bool IsSpawnerPreset(string assetId) =>
        SpawnerRoots.Any(r => assetId.StartsWith(r, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the asset is a known non-item entry (recipe/ingredient/icon/chopping).</summary>
    internal static bool IsNonItem(string assetId)
    {
        var name = AssetName(assetId);
        return NonItemPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string AssetName(string assetId)
    {
        var slash = assetId.LastIndexOf('/');
        return slash >= 0 ? assetId[(slash + 1)..] : assetId;
    }

    /// <summary>One screen row per real item: only the item file (its name ends in <c>_ES</c>), shown
    /// without the trailing "ES". The item's class file (the same name without <c>_ES</c>) is not an
    /// editable item and is not listed; it still serves as the shape donor when a field is added.</summary>
    public IReadOnlyList<ItemSummary> MapItems(IReadOnlyList<AssetSummary> assets) =>
        assets
            .Where(a => IsEditableItem(a.Id) && !IsNonItem(a.Id) && !IsBaseElement(a.Id) && !IsSpawnerPreset(a.Id))
            .Select(a => new ItemSummary(
                name: FriendlyItemName(a.Id),
                category: string.IsNullOrWhiteSpace(a.Category) ? "Uncategorized" : a.Category,
                description: a.TechnicalPath,
                editable: true,
                assetId: a.Id))
            .ToList();

    /// <summary>An item file's shown name: the file name without its <c>_ES</c>, underscores as spaces
    /// (Tomato_Seed_Bag_ES -> "Tomato Seed Bag").</summary>
    internal static string FriendlyItemName(string assetId)
    {
        var name = AssetName(assetId);
        if (name.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
        return name.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// One screen row per base-building element (the well and its relatives). Their values live on the
    /// blueprint's default object, so every one of them is offered for editing (the safety rule still
    /// decides, per value, what may actually be changed).
    /// </summary>
    public IReadOnlyList<ItemSummary> MapBaseElements(IReadOnlyList<AssetSummary> assets) =>
        assets
            .Where(a => IsBaseElement(a.Id))
            .Select(a => new ItemSummary(
                name: AssetNames.BaseElementFriendly(AssetName(a.Id)),
                category: string.IsNullOrWhiteSpace(a.Category) ? "Base elements" : a.Category,
                description: a.TechnicalPath,
                editable: true,
                assetId: a.Id))
            .ToList();

    /// <summary>
    /// One screen row per crafting recipe (a <c>CR_*</c> data asset). A recipe has no default
    /// object, so its values are read with the writer's library. Its plain values are offered for
    /// editing; its shape (product, ingredients) is shown locked.
    /// </summary>
    public IReadOnlyList<ItemSummary> MapRecipes(IReadOnlyList<AssetSummary> assets) =>
        assets
            .Where(a => IsRecipe(a.Id))
            .Select(a => new ItemSummary(
                // The name the index holds is the name the build resolves by — never re-derive it,
                // or a recipe would build as "not found".
                name: a.Name,
                category: AssetNames.RecipeCategory(a.TechnicalPath),
                description: a.TechnicalPath,
                editable: true,
                assetId: a.Id))
            .ToList();

    /// <summary>
    /// One screen row per loot/spawn preset (spawner presets and container loot — Task 2.6). The
    /// reader gives these a friendly name and the game's own folder as a category; the per-value
    /// safety rule still decides what may actually be changed.
    /// </summary>
    public IReadOnlyList<ItemSummary> MapSpawnerPresets(IReadOnlyList<AssetSummary> assets) =>
        assets
            .Where(a => IsSpawnerPreset(a.Id))
            .Select(a => new ItemSummary(
                name: a.Name,
                category: string.IsNullOrWhiteSpace(a.Category) ? "Loot & spawn" : a.Category,
                description: a.TechnicalPath,
                editable: true,
                assetId: a.Id))
            .ToList();

    /// <summary>
    /// One family's screen rows from the whole index — the one place a kind maps to its rows, so the
    /// Items screens and the Workspace cannot drift apart.
    /// </summary>
    public IReadOnlyList<ItemSummary> Map(AssetFamily family, IReadOnlyList<AssetSummary> assets) => family switch
    {
        AssetFamily.BaseElements => MapBaseElements(assets),
        AssetFamily.Recipes => MapRecipes(assets),
        AssetFamily.LootAndSpawn => MapSpawnerPresets(assets),
        _ => MapItems(assets),
    };

    /// <summary>The cached icon path for an asset id, or null when it has no cached icon.</summary>
    public string? IconPath(string assetId) => _icons?.GetIconPath(assetId);
}
