using System.Text.Json.Serialization;

namespace SCMW.Core.Editing;

/// <summary>
/// What kind of asset a project creates. Starts with recipes (the creation ladder's level 3);
/// categories and whole new objects (levels 4–5) can be added here later.
/// </summary>
public enum NewAssetKind
{
    /// <summary>A brand-new crafting recipe.</summary>
    Recipe,

    /// <summary>A brand-new item: the item file plus its <c>_ES</c> twin.</summary>
    Item,

    /// <summary>A brand-new ingredient category (a <c>CI_*</c> tag asset).</summary>
    Category,
}

/// <summary>
/// A brand-new asset a project creates: its own name, what kind it is, the vanilla asset it is a
/// renamed copy of (the donor), and the mod's own folder it lives in so it cannot clash with the game
/// or another mod. The asset's field edits are ordinary change records aimed at
/// <see cref="PackagePath"/>.
/// </summary>
/// <param name="Id">Stable identity; adding the same id twice is ignored, and it is what an undo
/// removes.</param>
/// <param name="Name">The new asset's own name, e.g. <c>CR_OlivePress</c>.</param>
/// <param name="Kind">What kind of asset it is (see <see cref="NewAssetKind"/>).</param>
/// <param name="DonorName">The vanilla asset it is a renamed copy of (shown to the player).</param>
/// <param name="DonorPath">The donor's exact game path, so two same-named donors can never be mixed up.</param>
/// <param name="Folder">The mod's own folder (its "namespace") the new file lives in.</param>
/// <param name="CreatedUtc">When the new asset was recorded, in UTC.</param>
public sealed record NewAsset(
    Guid Id,
    string Name,
    NewAssetKind Kind,
    string DonorName,
    string DonorPath,
    string Folder,
    DateTime CreatedUtc)
{
    /// <summary>
    /// The new asset's package path (its folder plus its name) — what the build clones the donor into
    /// and what the asset's own field edits are recorded against.
    /// </summary>
    [JsonIgnore]
    public string PackagePath => Folder + "/" + Name;

    /// <summary>
    /// An ITEM is two files — the item and its <c>_ES</c> twin — so the owner's edits are recorded
    /// against the twin's path. A recipe is one file, so its edit path is its own package path.
    /// </summary>
    [JsonIgnore]
    public string EditPath => Kind == NewAssetKind.Item ? TwinPackagePath! : PackagePath;

    /// <summary>The item's twin is the donor's <c>_ES</c> sibling; null for a recipe (a single file).</summary>
    [JsonIgnore]
    public string? TwinDonorPath => Kind == NewAssetKind.Item ? DonorPath + "_ES" : null;

    /// <summary>The new item's twin name (its name plus <c>_ES</c>); null for a recipe.</summary>
    [JsonIgnore]
    public string? TwinName => Kind == NewAssetKind.Item ? Name + "_ES" : null;

    /// <summary>The new item's twin package path (its folder plus its twin name); null for a recipe.</summary>
    [JsonIgnore]
    public string? TwinPackagePath => Kind == NewAssetKind.Item ? Folder + "/" + TwinName : null;
}

/// <summary>
/// Names the mod's own folder for a new asset: the donor's folder plus a short slug from the project
/// name, so every mod's new content sits in its own place and cannot collide with the game or another
/// mod. The owner never has to invent a folder name.
/// </summary>
public static class NewAssetNaming
{
    /// <summary>
    /// A folder-name-safe slug of the project name: letters and digits only, so it is safe on every
    /// file system. Never empty — a name with nothing usable falls back to <c>Mod</c>.
    /// </summary>
    public static string Slug(string projectName)
    {
        var kept = new string((projectName ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return kept.Length > 0 ? kept : "Mod";
    }

    /// <summary>
    /// The folder a new asset lives in: the donor's folder (a trailing slash is ignored) plus the
    /// project's own slug, e.g. <c>…/Recipes/Items</c> + "Olive Press" →
    /// <c>…/Recipes/Items/SCMW_OlivePress</c>.
    /// </summary>
    public static string FolderFor(string donorFolder, string projectName)
    {
        var trimmed = (donorFolder ?? string.Empty).TrimEnd('/');
        var slug = "SCMW_" + Slug(projectName);
        return trimmed.Length > 0 ? trimmed + "/" + slug : slug;
    }

    /// <summary>
    /// The recipe's own name from the plain name the owner typed: <c>CR_</c> plus the name's letters
    /// and digits, so the game always sees a valid recipe name. A leading <c>CR</c> the owner already
    /// typed is dropped rather than doubled (<c>"CR_Olive Press"</c> → <c>CR_OlivePress</c>), but a
    /// word that merely begins with those letters keeps them (<c>"Crate"</c> → <c>CR_Crate</c>).
    /// Null when the name has nothing usable to build a recipe name from.
    /// </summary>
    public static string? RecipeName(string? plainName)
    {
        var text = (plainName ?? string.Empty).Trim();

        // Drop a "CR" the owner typed as a prefix — either on its own or followed by a separator —
        // so it is never doubled, and never the letters of a word that merely begins with "CR"
        // (so "Crate" keeps its letters).
        if (text.Length == 2 && text.Equals("CR", StringComparison.OrdinalIgnoreCase))
        {
            text = string.Empty;
        }
        else if (text.Length >= 3 && text.StartsWith("CR", StringComparison.OrdinalIgnoreCase)
                 && (text[2] == '_' || text[2] == ' ' || text[2] == '-'))
        {
            text = text[3..];
        }

        var kept = new string(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length > 0 ? "CR_" + kept : null;
    }

    /// <summary>
    /// An item's own name from the plain name the owner typed: the name's letters and digits (SCUM's
    /// item names carry no prefix, so neither does ours). Null when there is nothing usable.
    /// </summary>
    public static string? ItemName(string? plainName)
    {
        var kept = new string((plainName ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return kept.Length > 0 ? kept : null;
    }

    /// <summary>
    /// A readable name for a technical asset name the owner made up: <c>OliveOil</c> →
    /// <c>Olive Oil</c>, <c>CrudeOil2</c> → <c>Crude Oil 2</c>. Used only for the list row of a new
    /// project asset, never for the name written to the game.
    /// </summary>
    public static string FriendlyName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        var kept = new System.Text.StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '_')
            {
                kept.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c))
            {
                var previous = text[i - 1];
                var nextIsLower = i + 1 < text.Length && char.IsLower(text[i + 1]);
                if (char.IsLower(previous) || (char.IsUpper(previous) && nextIsLower))
                    kept.Append(' ');
            }
            else if (i > 0 && char.IsDigit(c) && char.IsLetter(text[i - 1]))
            {
                kept.Append(' ');
            }

            kept.Append(c);
        }

        return string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// A category's own name from the plain name the owner typed: <c>CI_</c> plus its letters and
    /// digits, so the game always sees a valid ingredient tag. A leading <c>CI</c> the owner already
    /// typed is dropped rather than doubled (<c>"CI Olives"</c> → <c>CI_Olives</c>). Null when the
    /// name has nothing usable.
    /// </summary>
    public static string? CategoryName(string? plainName)
    {
        var text = (plainName ?? string.Empty).Trim();

        if (text.Length == 2 && text.Equals("CI", StringComparison.OrdinalIgnoreCase))
        {
            text = string.Empty;
        }
        else if (text.Length >= 3 && text.StartsWith("CI", StringComparison.OrdinalIgnoreCase)
                 && (text[2] == '_' || text[2] == ' ' || text[2] == '-'))
        {
            text = text[3..];
        }

        var kept = new string(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length > 0 ? "CI_" + kept : null;
    }
}
