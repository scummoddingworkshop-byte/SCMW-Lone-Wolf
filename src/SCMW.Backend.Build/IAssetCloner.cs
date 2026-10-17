namespace SCMW.Backend.Build;

/// <summary>
/// Renames a copied package's own object(s) so the copy is a distinct asset. The file must already
/// sit under the new asset's name (see <see cref="CloneLayout"/>); only the names <i>inside</i> the
/// package change here.
/// </summary>
public interface IAssetCloner
{
    /// <summary>Renames <paramref name="oldName"/> (and its class-default twin) to
    /// <paramref name="newName"/> inside the package, then saves it. Never throws.</summary>
    bool TryRenameObjects(string uassetFilePath, string oldName, string newName, out string? error);
}
