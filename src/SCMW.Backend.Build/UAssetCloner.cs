using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// The real cloner: opens a copied package with UAssetAPI and renames its object(s) — the asset's
/// own object and its class-default twin (<c>Default__Name_C</c>) — so the clone is a new asset
/// rather than a second copy of the donor. Proven by the probe. Never throws; a package with no
/// object of that name is a plain failure.
/// </summary>
public sealed class UAssetCloner : IAssetCloner
{
    public bool TryRenameObjects(string uassetFilePath, string oldName, string newName, out string? error)
    {
        error = null;
        try
        {
            var asset = new UAsset(uassetFilePath, EngineVersion.VER_UE4_27);
            var cdoOld = $"Default__{oldName}_C";
            var cdoNew = $"Default__{newName}_C";

            var renamed = 0;
            foreach (var export in asset.Exports)
            {
                var current = export.ObjectName.ToString();
                if (string.Equals(current, oldName, StringComparison.Ordinal))
                {
                    export.ObjectName = FName.FromString(asset, newName);
                    renamed++;
                }
                else if (string.Equals(current, cdoOld, StringComparison.Ordinal))
                {
                    export.ObjectName = FName.FromString(asset, cdoNew);
                    renamed++;
                }
            }

            if (renamed == 0)
            {
                error = $"No object named '{oldName}' was found inside '{Path.GetFileName(uassetFilePath)}' to rename.";
                return false;
            }

            asset.Write(uassetFilePath);
            return true;
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Cloning a game file failed while renaming ({ex.GetType().Name}).");
            error = $"The copy of '{oldName}' could not be renamed.";
            return false;
        }
    }
}
