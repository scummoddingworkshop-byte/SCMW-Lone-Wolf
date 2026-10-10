using System;
using System.Collections.Generic;
using System.Linq;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Knowledge;

namespace SCMW.App;

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

    /// <summary>One screen row per real item, with non-items left out.</summary>
    public IReadOnlyList<ItemSummary> MapItems(IReadOnlyList<AssetSummary> assets) =>
        assets
            .Where(a => !IsNonItem(a.Id))
            .Select(a => new ItemSummary(
                name: a.Name,
                category: string.IsNullOrWhiteSpace(a.Category) ? "Uncategorized" : a.Category,
                description: a.TechnicalPath,
                editable: IsEditableItem(a.Id),
                assetId: a.Id))
            .ToList();

    /// <summary>
    /// One screen value per real field: a friendly label from <see cref="FieldNames"/>, a group
    /// from <see cref="FieldKnowledge"/>, a plain value from <see cref="FieldKnowledge"/>. The
    /// fields are changeable only for an <c>_ES</c> item (the editable copy) — the engine's own
    /// status is honoured as a fallback so a future proven-editable field still shows.
    /// </summary>
    public IReadOnlyList<ItemValue> MapFields(IReadOnlyList<AssetField> fields, bool editableItem = false) =>
        fields.Select(f => new ItemValue(
            name: FieldNames.Friendly(f.TechnicalName),
            group: FieldKnowledge.GroupFor(f.TechnicalName).ToString(),
            value: FieldKnowledge.FormatValue(f.CurrentValue),
            kind: KindFor(f.ValueTypeHint),
            editable: (editableItem && FieldSafety.For(f.TechnicalName, f.ValueTypeHint) == EditSafety.Safe)
                      || f.Status == StatusKind.Editable,
            technicalName: f.TechnicalName)).ToList();

    /// <summary>The editor kind for a field, from its UE type — so booleans and text validate correctly.</summary>
    private static ItemValueKind KindFor(string? typeHint) => typeHint switch
    {
        var h when h is not null && h.Contains("Bool", StringComparison.OrdinalIgnoreCase) => ItemValueKind.Boolean,
        var h when h is not null && (h.Contains("Str", StringComparison.OrdinalIgnoreCase)
                                     || h.Contains("Text", StringComparison.OrdinalIgnoreCase)
                                     || h.Contains("Name", StringComparison.OrdinalIgnoreCase)) => ItemValueKind.Text,
        _ => ItemValueKind.Number,
    };

    /// <summary>The cached icon path for an asset id, or null when it has no cached icon.</summary>
    public string? IconPath(string assetId) => _icons?.GetIconPath(assetId);
}
