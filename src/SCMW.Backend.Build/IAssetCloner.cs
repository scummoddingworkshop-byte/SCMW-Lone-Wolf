namespace SCMW.Backend.Build;

/// <summary>
/// Renames a copied package's own object(s) so the copy is a distinct asset, and re-points any soft
/// reference that still names the donor at the copy. The file must already sit under the new asset's
/// name (see <see cref="CloneLayout"/>); only what is <i>inside</i> the package changes here.
/// </summary>
public interface IAssetCloner
{
    /// <summary>Renames <paramref name="oldName"/> (and its class-default twin) to
    /// <paramref name="newName"/> inside the package, re-points soft references that name a donor
    /// asset in <paramref name="references"/> at its copy, then saves it. Never throws.</summary>
    bool TryRenameObjects(string uassetFilePath, string oldName, string newName,
                          IReadOnlyList<AssetReferenceRemap> references, out string? error);
}
