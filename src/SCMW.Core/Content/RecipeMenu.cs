// src/SCMW.Core/Content/RecipeMenu.cs
// The crafting menu's shelves and their order. A recipe's shelf is the value of its
// CraftingCategoryTag.TagName (e.g. CraftingCategory.Items.Clothing.ClothingRepairs); the order
// inside a block is its DisplayPriority, lower first. The list of recipes itself is read from the
// installed game by the GameModel tool and shipped as recipe-groups.json.
using System.Text;

namespace SCMW.Core.Content;

/// <summary>One recipe's place in the menu: the shelf it sits on and its number on that shelf.</summary>
public sealed record RecipePlace(string Tag, string Recipe, int Priority);

/// <summary>A shelf's name in plain words: <c>Clothing → Clothing Repairs</c>, or <c>Melee Weapons</c>
/// when the shelf has no separate block.</summary>
public sealed record MenuShelf(string Tag, string Tab, string Group, string? SubGroup, string Label);

/// <summary>One shelf s worth of recipes, earliest first.</summary>
public sealed record RecipeGroup(string Tag, string Tab, string Group, string? SubGroup, string Label,
    IReadOnlyList<RecipePlace> Recipes);

/// <summary>Reading and shaping the menu's shelves, kept pure and UI-free.</summary>
public static class RecipeMenu
{
    /// <summary>The game stores a recipe's order in one byte, so 255 is the highest number it can take.</summary>
    public const int TopPriority = 255;

    private const string TagPrefix = "CraftingCategory.";

    /// <summary>
    /// The shelf a tag names, in plain words; null when the value is not a menu shelf (a <c>CI_*</c>
    /// ingredient tag, a bare word, or too few parts).
    /// </summary>
    public static MenuShelf? GroupOf(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim();
        if (!text.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var parts = text[TagPrefix.Length..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var tab = Words(parts[0]);
        var group = Words(parts[1]);
        var subGroup = parts.Length > 2 ? Words(string.Join(".", parts[2..])) : null;
        var label = string.IsNullOrEmpty(subGroup) ? group : $"{group} → {subGroup}";
        return new MenuShelf(text, tab, group, subGroup, label);
    }

    /// <summary>The recipes gathered onto their shelves, shelves in a stable order, each shelf's
    /// recipes earliest first. Unreadable tags are skipped, never guessed at.</summary>
    public static IReadOnlyList<RecipeGroup> Groups(IEnumerable<RecipePlace>? places)
    {
        if (places is null) return Array.Empty<RecipeGroup>();

        var gathered = new Dictionary<string, (MenuShelf Shelf, List<RecipePlace> Recipes)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var place in places)
        {
            if (place is null) continue;

            var shelf = GroupOf(place.Tag);
            if (shelf is null) continue;

            if (!gathered.TryGetValue(shelf.Tag, out var entry))
            {
                entry = (shelf, new List<RecipePlace>());
                gathered[shelf.Tag] = entry;
            }

            entry.Recipes.Add(place);
        }

        var groups = new List<RecipeGroup>(gathered.Count);
        foreach (var (shelf, recipes) in gathered.Values)
        {
            var ordered = recipes
                .OrderBy(r => r.Priority)
                .ThenBy(r => RecipeLabel(r.Recipe), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Recipe, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            groups.Add(new RecipeGroup(shelf.Tag, shelf.Tab, shelf.Group, shelf.SubGroup, shelf.Label,
                ordered));
        }

        return groups
            .OrderBy(g => g.Tab, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.SubGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>A recipe's plain words: <c>CR_Improvised_Sewing_Kit</c> → <c>Improvised Sewing Kit</c>.</summary>
    public static string RecipeLabel(string? recipeName)
    {
        if (string.IsNullOrWhiteSpace(recipeName)) return string.Empty;

        var label = recipeName.Trim();
        if (label.StartsWith("CR_", StringComparison.OrdinalIgnoreCase)) label = label[3..];
        return Collapse(label.Replace('_', ' ')).Trim();
    }

    /// <summary>The number to put a recipe at the end of a shelf; null when the top number is taken.</summary>
    public static int? AtEnd(IReadOnlyList<RecipePlace>? recipes)
    {
        var next = recipes is null || recipes.Count == 0 ? 0 : recipes.Max(r => r.Priority) + 1;
        return next <= TopPriority ? next : null;
    }

    /// <summary>
    /// The first number after <paramref name="priority"/> that no recipe on the shelf already uses;
    /// null when that number is the top one or nothing is left above it. The app never renumbers a
    /// recipe the owner did not ask about, so a taken spot makes the new recipe land at the next gap.
    /// </summary>
    public static int? After(IReadOnlyList<RecipePlace>? recipes, int priority)
    {
        if (priority < 0 || priority >= TopPriority) return null;

        var used = recipes?.Select(r => r.Priority).ToHashSet() ?? new HashSet<int>();
        for (var number = priority + 1; number <= TopPriority; number++)
        {
            if (!used.Contains(number)) return number;
        }

        return null;
    }

    /// <summary>Words from a tag part: <c>ClothesAndAccessories</c> → <c>Clothes And Accessories</c>.</summary>
    private static string Words(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = raw.Trim();
        var words = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '.' or '_' or ' ')
            {
                if (words.Length > 0 && words[^1] != ' ') words.Append(' ');
                continue;
            }

            if (char.IsUpper(c) && i > 0 && words.Length > 0 && words[^1] != ' ')
            {
                var previous = text[i - 1];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                var afterLower = char.IsLower(previous) || char.IsDigit(previous);
                var endOfRun = char.IsUpper(previous) && char.IsLower(next);
                if (afterLower || endOfRun) words.Append(' ');
            }

            words.Append(c);
        }

        return Collapse(words.ToString()).Trim();
    }

    /// <summary>One space between words, never two.</summary>
    private static string Collapse(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == ' ' && clean.Length > 0 && clean[^1] == ' ') continue;
            clean.Append(c);
        }

        return clean.ToString();
    }
}
