namespace SCMW.GameModel;

/// <summary>
/// The seven gameplay families of the first Game Model sweep, and the rule that
/// puts a pak file into one. Rules are the same naming/folder habits used by the
/// earlier sampled extract (families.json) and the app's own item / base-element /
/// spawner roots. A path that matches nothing (or is not a .uasset) is not ours.
/// </summary>
internal static class FamilyRules
{
    /// <summary>The families, in the order they are read and written out.</summary>
    internal static readonly IReadOnlyList<string> Families = new[]
    {
        "items", "recipes", "base_elements", "crafting_tags", "chopping", "spawners", "string_tables",
    };

    /// <summary>The family a pak file belongs to, or <c>null</c> if it is none of them.</summary>
    internal static string? FamilyOf(string pakPath)
    {
        if (string.IsNullOrEmpty(pakPath)) return null;
        if (!pakPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return null;

        var name = FileName(pakPath)[..^".uasset".Length];   // the asset name, no extension or folder

        // Name-prefixed families first (unambiguous), then the folder-based ones.
        if (name.StartsWith("CR_", StringComparison.OrdinalIgnoreCase)) return "recipes";
        if (name.StartsWith("CI_", StringComparison.OrdinalIgnoreCase)) return "crafting_tags";
        if (name.StartsWith("Chop_", StringComparison.OrdinalIgnoreCase)) return "chopping";
        if (Contains(pakPath, "/BaseBuilding/BaseElements/")) return "base_elements";
        if (Contains(pakPath, "/SpawnerPresets/") || Contains(pakPath, "/SpawnerPresets2/")) return "spawners";
        if (Contains(pakPath, "/StringTables/") || name.StartsWith("ST_", StringComparison.OrdinalIgnoreCase)) return "string_tables";
        if (name.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)) return "items";

        return null;
    }

    private static string FileName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
