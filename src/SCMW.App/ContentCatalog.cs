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

    /// <summary>
    /// Fields that carry no owner meaning in game and so are not offered. A base element's
    /// <c>_name</c> is the case we know: it builds and writes, but the name the game shows comes
    /// from the element's type/class, so editing it changes nothing visible (owner, 2026-10-12).
    /// </summary>
    private static readonly IReadOnlySet<string> HiddenFields =
        new HashSet<string>(new[] { "_name" }, StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// One screen value per real field: a friendly label from <see cref="FieldNames"/>, a group
    /// from <see cref="FieldKnowledge"/>, a plain value from <see cref="FieldKnowledge"/>. Each
    /// field is sorted into a band by <see cref="ValueSafety"/>: an ordinary field (editable), a
    /// noted grey-area field (editable with a one-line caveat), or a locked one (shown with a
    /// plain reason, not offered). A nested value shares its parent struct's heading and shows
    /// just its own name (<c>Max Amount</c>), not the whole path.
    /// </summary>
    public IReadOnlyList<ItemValue> MapFields(IReadOnlyList<AssetField> fields, bool editableItem = false) =>
        fields.Where(f => !HiddenFields.Contains(f.TechnicalName)).Select(f =>
        {
            var band = ValueSafety.For(f.TechnicalName, f.ValueTypeHint);
            return new ItemValue(
                name: LabelFor(f),
                group: GroupFor(f),
                value: FieldKnowledge.FormatValue(f.CurrentValue),
                kind: KindFor(f.ValueTypeHint),
                editable: f.Status == StatusKind.Editable
                          || (editableItem && band is ValueBand.Normal or ValueBand.Noted),
                technicalName: f.TechnicalName,
                band: band,
                note: band == ValueBand.Noted ? NoteFor(f.ValueTypeHint) : null);
        }).ToList();

    /// <summary>A one-line caveat for a noted (grey-area) value, from its UE type.</summary>
    private static string? NoteFor(string? typeHint)
    {
        var hint = typeHint ?? string.Empty;
        if (hint.Contains("Enum", StringComparison.OrdinalIgnoreCase))
            return "A named choice — type one of the game's exact names (an invented one won't work).";
        if (hint.Contains("Byte", StringComparison.OrdinalIgnoreCase))
            return "A number the game reads (0–255), or a name it matches exactly.";
        if (hint.Contains("Object", StringComparison.OrdinalIgnoreCase))
            return "A link — type the exact name of a game asset that already exists.";
        if (hint.Contains("Name", StringComparison.OrdinalIgnoreCase))
            return "The game matches this name exactly — type it carefully.";
        return null;
    }

    /// <summary>
    /// The section a field shows under. A value inside an ingredient slot sits under that slot
    /// ("Ingredient 1"), so it can be managed as a unit; other nested values sit under their
    /// parent's friendly trail; a plain field uses its usual group.
    /// </summary>
    private static string GroupFor(AssetField field)
    {
        if (!field.Nested)
            return FieldKnowledge.GroupFor(field.TechnicalName).ToString();

        if (SlotIndex(field.TechnicalName) is { } slot)
            return "Ingredient " + (slot + 1);

        var dot = field.TechnicalName.LastIndexOf('.');
        var parent = dot > 0 ? field.TechnicalName[..dot] : string.Empty;
        return parent.Length == 0 ? "Other" : FriendlyPath(parent);
    }

    /// <summary>A nested value's own short label, with a couple of plain aliases.</summary>
    private static string LabelFor(AssetField field)
    {
        if (!field.Nested) return FieldNames.Friendly(field.TechnicalName);

        // Inside an ingredient slot, keep the shape ("Amount › No Skill") — the slot is the heading.
        if (SlotIndex(field.TechnicalName) is not null)
        {
            var rest = SubPath(field.TechnicalName);
            var open = rest.IndexOf('[');
            if (open > 0 && rest.EndsWith(']')
                && rest[..open].Equals("AllowedTypes", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(rest[(open + 1)..^1], out var tag))
            {
                return "Accepts " + (tag + 1);
            }
            return FriendlyPath(rest);
        }

        return FieldNames.Friendly(LeafPath(field.TechnicalName));
    }

    /// <summary>The ingredient slot a path belongs to ("Ingredients[2]…" → 2), or null.</summary>
    private static int? SlotIndex(string path)
    {
        const string prefix = "Ingredients[";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var close = path.IndexOf(']', prefix.Length);
        return close > 0 && int.TryParse(path[prefix.Length..close], out var index) ? index : null;
    }

    /// <summary>The path after its ingredient-slot step ("Ingredients[0].Amount.NoSkill" → "Amount.NoSkill").</summary>
    private static string SubPath(string path)
    {
        var close = path.IndexOf(']');
        return close >= 0 && close + 1 < path.Length && path[close + 1] == '.'
            ? path[(close + 2)..]
            : path;
    }

    /// <summary>Renders a dotted path as a breadcrumb ("Ingredients[0].Amount" → "Ingredient 1 › Amount").</summary>
    private static string FriendlyPath(string path) =>
        string.Join(" › ", path.Split('.').Select(Segment));

    /// <summary>Renders one path step; an array step ("Ingredients[0]") becomes "Ingredient 1".</summary>
    private static string Segment(string step)
    {
        var bracket = step.IndexOf('[');
        if (bracket >= 0 && step.EndsWith(']'))
        {
            var label = FieldNames.Friendly(step[..bracket]);
            if (label.EndsWith('s') && label.Length > 1) label = label[..^1];   // a simple plural
            var index = step[(bracket + 1)..^1];
            return int.TryParse(index, out var n) ? $"{label} {n + 1}" : label;
        }
        return FieldNames.Friendly(step);
    }

    /// <summary>The last step of a dotted path — a nested value's own name ("Replenish Amount").</summary>
    private static string LeafPath(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot >= 0 && dot + 1 < path.Length ? path[(dot + 1)..] : path;
    }

    /// <summary>The editor kind for a field, from its UE type — so booleans and text validate correctly.</summary>
    private static ItemValueKind KindFor(string? typeHint) => typeHint switch
    {
        var h when h is not null && h.Contains("Bool", StringComparison.OrdinalIgnoreCase) => ItemValueKind.Boolean,
        var h when h is not null && (h.Contains("Str", StringComparison.OrdinalIgnoreCase)
                                     || h.Contains("Text", StringComparison.OrdinalIgnoreCase)
                                     || h.Contains("Name", StringComparison.OrdinalIgnoreCase)
                                     || h.Contains("Enum", StringComparison.OrdinalIgnoreCase)
                                     || h.Contains("Object", StringComparison.OrdinalIgnoreCase)) => ItemValueKind.Text,
        _ => ItemValueKind.Number,
    };

    /// <summary>The cached icon path for an asset id, or null when it has no cached icon.</summary>
    public string? IconPath(string assetId) => _icons?.GetIconPath(assetId);
}
