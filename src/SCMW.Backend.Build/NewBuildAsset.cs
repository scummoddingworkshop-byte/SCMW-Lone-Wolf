namespace SCMW.Backend.Build;

/// <summary>
/// One brand-new asset a build must create: the vanilla donor it is a renamed copy of, where the
/// copy goes (the asset's own folder and name), and any ordinary field edits aimed at it. The build
/// clones the donor under the new name and adds one entry for the asset to the game's index.
/// </summary>
/// <param name="Name">The new asset's own name, e.g. <c>CR_OlivePress</c>.</param>
/// <param name="DonorName">The donor's name as shown to the owner (for plain messages). The donor's
/// real file name is taken from <paramref name="DonorPath"/>, never from this.</param>
/// <param name="DonorPath">The donor's exact game path — the donor's identity, and where the build
/// reads the file and derives the name to rename the copy from.</param>
/// <param name="Folder">The folder the clone is laid down in (the mod's own namespace).</param>
/// <param name="Changes">The asset's own field edits: written onto its twin when it has one (an item's
/// editable file is its <c>_ES</c> twin), else onto the asset itself.</param>
/// <param name="TwinName">For an item, the name of its <c>_ES</c> twin (a second file to clone);
/// null for a recipe (a single file).</param>
/// <param name="TwinDonorPath">For an item, the twin's donor path; null for a recipe.</param>
public sealed record NewBuildAsset(
    string Name,
    string DonorName,
    string DonorPath,
    string Folder,
    IReadOnlyList<FieldChange> Changes,
    string? TwinName = null,
    string? TwinDonorPath = null);
