namespace SCMW.Core.Editing;

/// <summary>
/// One section of a project's own on the game's crafting menu — "SCMW Mods" — recorded once and
/// reused by any of the project's new recipes. Only the Items rail is supported for now.
/// </summary>
/// <param name="Id">Stable identity; adding the same id twice is ignored, and it is what an undo removes.</param>
/// <param name="Name">The words the menu shows, e.g. <c>SCMW Mods</c>.</param>
/// <param name="Tag">The game tag behind the section, e.g. <c>CraftingCategory.Items.SCMW_Mods</c>.</param>
/// <param name="CreatedUtc">When the section was recorded, in UTC.</param>
public sealed record CraftingSection(Guid Id, string Name, string Tag, DateTime CreatedUtc);

/// <summary>Turns the owner's words into the game's tag for a new Items menu section.</summary>
public static class CraftingSectionNaming
{
    /// <summary>The game's root for an Items menu section.</summary>
    public const string ItemsPrefix = "CraftingCategory.Items.";

    /// <summary>
    /// The section's tag from the plain name the owner typed: every run of anything that is not a
    /// letter or digit becomes one underscore, under the Items root. <c>"SCMW Mods"</c> →
    /// <c>CraftingCategory.Items.SCMW_Mods</c>. Null when the name has nothing usable.
    /// </summary>
    public static string? TagFor(string? plainName)
    {
        var words = SplitWords(plainName);
        return words.Count == 0 ? null : ItemsPrefix + string.Join('_', words);
    }

    /// <summary>The words shown in the menu: the owner's name, trimmed and with runs of spaces
    /// collapsed, never empty.</summary>
    public static string WordsFor(string? plainName)
    {
        var words = SplitWords(plainName);
        return string.Join(' ', words);
    }

    /// <summary>True when <paramref name="tag"/> already belongs to one of <paramref name="existingTags"/>,
    /// compared without case (the game treats tags that way).</summary>
    public static bool IsTaken(string? tag, IEnumerable<string> existingTags)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return existingTags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> SplitWords(string? plainName)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in plainName ?? string.Empty)
        {
            if (c < 128 && char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }
}
