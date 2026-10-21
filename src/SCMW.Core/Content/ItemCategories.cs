// src/SCMW.Core/Content/ItemCategories.cs
// What an item "counts as": the ingredient categories (CI_* tags) the game matches a recipe's
// ingredient list against. A pure data model — no UI, no files. The same field paths are built
// here and nowhere else, mirroring how RecipeIngredients owns a recipe's slot paths.
using System.Globalization;
using SCMW.Core.Editing;

namespace SCMW.Core.Content;

/// <summary>
/// A selectable ingredient category: <paramref name="Name"/> is the tag the game stores
/// (<c>CI_Olives</c>), <paramref name="Path"/> is where its own file lives (a pak path when the
/// category comes from the game, or the mod's new copy), and <paramref name="Label"/> is the plain
/// words the owner reads.
/// </summary>
public sealed record CategoryChoice(string Name, string Label, string Path) : IPickerItem;

/// <summary>One recorded add/remove change to an item's category list, in recorded order.</summary>
public sealed record ItemCategoryListChange(ChangeOperation Operation, int Index);

/// <summary>One entry of an item's category list: the category it points at and how much it counts.</summary>
public sealed record ItemCategorySlot(int Index, AssetField? Tag, AssetField? Amount)
{
    /// <summary>The category's plain tag name (<c>CI_Olives</c>), or null.</summary>
    public string? TagName => ItemCategories.NameOf(Tag?.CurrentValue);

    /// <summary>The amount the entry counts for (usually 1), or null when the file has none.</summary>
    public string? AmountValue => Amount?.CurrentValue;
}

/// <summary>
/// Groups an item's nested <c>CraftingIngredientInstances[N]…</c> fields into one entry per index.
/// This is the deliberate, bounded exception to the field-library rule for this list, exactly like
/// <see cref="RecipeIngredients"/>: these per-entry paths cannot be named by the fixed library, so
/// the one place they are built lives here.
/// </summary>
public static class ItemCategories
{
    /// <summary>The list field an item's categories live in.</summary>
    public const string ListName = "CraftingIngredientInstances";

    /// <summary>The category link inside one entry.</summary>
    private const string TypeField = "Type";

    /// <summary>The amount inside one entry.</summary>
    private const string AmountField = "Amount";

    /// <summary>The item's category entries, ordered by list index. Never null.</summary>
    public static IReadOnlyList<ItemCategorySlot> From(IReadOnlyList<AssetField>? fields)
    {
        if (fields is null || fields.Count == 0)
            return Array.Empty<ItemCategorySlot>();

        var entries = new SortedDictionary<int, (AssetField? Tag, AssetField? Amount)>();
        foreach (var field in fields)
        {
            if (!TrySlot(field.TechnicalName, out var index, out var rest))
                continue;

            entries.TryGetValue(index, out var entry);
            if (string.Equals(rest, TypeField, StringComparison.OrdinalIgnoreCase))
                entry.Tag = field;
            else if (string.Equals(rest, AmountField, StringComparison.OrdinalIgnoreCase))
                entry.Amount = field;
            else
                continue;

            entries[index] = entry;
        }

        var result = new List<ItemCategorySlot>(entries.Count);
        foreach (var (index, entry) in entries)
            result.Add(new ItemCategorySlot(index, entry.Tag, entry.Amount));
        return result;
    }

    /// <summary>
    /// The item's entries with the project's recorded list changes played back in order: an add
    /// appends a clone of the last entry, a remove drops the entry at its index. The result is
    /// renumbered from zero, matching how the build writes the list. Never null.
    /// </summary>
    public static IReadOnlyList<ItemCategorySlot> WithListChanges(
        IReadOnlyList<ItemCategorySlot>? slots, IReadOnlyList<ItemCategoryListChange>? changes)
    {
        var working = slots is null ? new List<ItemCategorySlot>() : new List<ItemCategorySlot>(slots);

        if (changes is not null)
        {
            foreach (var change in changes)
            {
                switch (change.Operation)
                {
                    case ChangeOperation.AddEntry:
                        working.Add(working.Count > 0
                            ? working[^1]
                            : new ItemCategorySlot(0, null, null));
                        break;

                    case ChangeOperation.RemoveEntry when change.Index >= 0 && change.Index < working.Count:
                        working.RemoveAt(change.Index);
                        break;
                }
            }
        }

        var result = new List<ItemCategorySlot>(working.Count);
        for (var i = 0; i < working.Count; i++)
            result.Add(working[i] with { Index = i });
        return result;
    }

    /// <summary>The game's real categories, from its indexed <c>CI_*</c> assets. Never null.</summary>
    public static IReadOnlyList<CategoryChoice> CategoriesFrom(IReadOnlyList<AssetSummary>? assets)
    {
        if (assets is null || assets.Count == 0)
            return Array.Empty<CategoryChoice>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new List<CategoryChoice>();
        foreach (var asset in assets)
        {
            var name = AssetName(asset);
            if (name is null || !name.StartsWith("CI_", StringComparison.OrdinalIgnoreCase) || !seen.Add(name))
                continue;

            categories.Add(new CategoryChoice(name, RecipeTags.Label(name), asset.Id));
        }

        categories.Sort(static (a, b) =>
        {
            var byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return categories;
    }

    /// <summary>The plain tag name from a value that may be a bare name or a whole object path.</summary>
    public static string? NameOf(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        var slash = text.LastIndexOf('/');
        if (slash >= 0) text = text[(slash + 1)..];
        return text.Length == 0 ? null : text;
    }

    /// <summary>The path of one entry, e.g. <c>CraftingIngredientInstances[2]</c> (for add/remove).</summary>
    public static string SlotPath(int slotIndex) =>
        ListName + "[" + slotIndex.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The path of one entry's category link, e.g. <c>CraftingIngredientInstances[2].Type</c>.</summary>
    public static string TagPath(int slotIndex) => SlotPath(slotIndex) + "." + TypeField;

    /// <summary>The path of one entry's amount, e.g. <c>CraftingIngredientInstances[2].Amount</c>.</summary>
    public static string AmountPath(int slotIndex) => SlotPath(slotIndex) + "." + AmountField;

    /// <summary>The entry index a saved path names, or false for a non-entry path.</summary>
    public static bool TrySlotIndex(string? path, out int index)
    {
        index = -1;
        return path is not null && TrySlot(path, out index, out _);
    }

    /// <summary>Split <c>CraftingIngredientInstances[3].Rest</c> into its index and the rest.</summary>
    private static bool TrySlot(string name, out int index, out string rest)
    {
        index = -1;
        rest = string.Empty;

        if (string.IsNullOrEmpty(name))
            return false;

        var prefix = ListName + "[";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var close = name.IndexOf(']', prefix.Length);
        if (close < 0)
            return false;

        if (!int.TryParse(name.AsSpan(prefix.Length, close - prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out index))
            return false;

        var afterClose = close + 1;
        rest = afterClose < name.Length && name[afterClose] == '.'
            ? name[(afterClose + 1)..]
            : string.Empty;
        return true;
    }

    private static string? AssetName(AssetSummary? asset)
    {
        if (asset is null || string.IsNullOrEmpty(asset.Id))
            return null;

        var slash = asset.Id.LastIndexOf('/');
        return slash >= 0 ? asset.Id[(slash + 1)..] : asset.Id;
    }
}
