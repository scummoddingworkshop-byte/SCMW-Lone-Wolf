namespace SCMW.Backend.Build;

/// <summary>
/// One brand-new asset a build must create: the vanilla donor it is a renamed copy of, where the
/// copy goes (the asset's own folder and name), and any ordinary field edits aimed at it. The build
/// clones the donor under the new name and adds one entry for the asset to the game's index.
/// </summary>
/// <param name="Name">The new asset's own name, e.g. <c>CR_OlivePress</c>.</param>
/// <param name="DonorName">The donor asset's own file name, e.g. <c>CR_1H_Stone_Axe</c>.</param>
/// <param name="DonorPath">The donor's exact game path, so the build reads the right file.</param>
/// <param name="Folder">The folder the clone is laid down in (the mod's own namespace).</param>
/// <param name="Changes">The asset's own field edits, applied to the clone after it is renamed.</param>
public sealed record NewBuildAsset(
    string Name,
    string DonorName,
    string DonorPath,
    string Folder,
    IReadOnlyList<FieldChange> Changes);
