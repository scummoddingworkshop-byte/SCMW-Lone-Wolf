namespace SCMW.Core.Editing;

/// <summary>
/// What kind of asset a project creates. Starts with recipes (the creation ladder's level 3);
/// categories and whole new objects (levels 4–5) can be added here later.
/// </summary>
public enum NewAssetKind
{
    /// <summary>A brand-new crafting recipe.</summary>
    Recipe,
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
    public string PackagePath => Folder + "/" + Name;
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
}
