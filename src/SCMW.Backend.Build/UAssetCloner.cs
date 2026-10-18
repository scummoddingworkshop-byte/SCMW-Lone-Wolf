using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// The real cloner: opens a copied package with UAssetAPI and renames its object(s) so the clone is a
/// new asset rather than a second copy of the donor. A Blueprint asset carries three names that must
/// move together: the object itself (<c>Name</c>), its generated class (<c>Name_C</c>) and that
/// class's default object (<c>Default__Name_C</c>). Renaming only some of them leaves the game with a
/// half-renamed asset that crashes on load. Never throws; a package with no object of that name is a
/// plain failure.
/// </summary>
public sealed class UAssetCloner : IAssetCloner
{
    public bool TryRenameObjects(string uassetFilePath, string oldName, string newName, out string? error)
    {
        error = null;
        try
        {
            var asset = new UAsset(uassetFilePath, EngineVersion.VER_UE4_27);

            var renames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [oldName] = newName,                                        // the object itself
                [$"{oldName}_C"] = $"{newName}_C",                          // its generated class
                [$"Default__{oldName}_C"] = $"Default__{newName}_C",        // that class's default object
            };

            var renamed = 0;
            foreach (var export in asset.Exports)
            {
                var current = export.ObjectName.ToString();
                if (renames.TryGetValue(current, out var replacement))
                {
                    export.ObjectName = FName.FromString(asset, replacement);
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
