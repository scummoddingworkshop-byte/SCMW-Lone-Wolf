using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// The real cloner: opens a copied package with UAssetAPI and renames its object(s) so the clone is a
/// new asset rather than a second copy of the donor. A Blueprint asset carries three names that must
/// move together: the object itself (<c>Name</c>), its generated class (<c>Name_C</c>) and that
/// class's default object (<c>Default__Name_C</c>). Renaming only some of them leaves the game with a
/// half-renamed asset that crashes on load.
/// <para>
/// A rename alone is not enough: a copy can still <i>point at</i> the donor through a soft reference.
/// An item's "_ES" companion names its item that way (its "Actor" property), so a copy that keeps it
/// stays the donor's setup and the new item gets none. The caller passes the remaps (each asset the
/// copy might name, and where its copy lives) and every soft reference is re-pointed.
/// </para>
/// Never throws; a package with no object of that name is a plain failure.
/// </summary>
public sealed class UAssetCloner : IAssetCloner
{
    public bool TryRenameObjects(string uassetFilePath, string oldName, string newName,
                                 IReadOnlyList<AssetReferenceRemap> references, out string? error)
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

            if (references.Count > 0)
                foreach (var export in asset.Exports.OfType<NormalExport>())
                    if (export.Data is not null)
                        RemapReferences(asset, export.Data, references);

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

    /// <summary>Re-points every soft reference in a property list (and inside its arrays and structs)
    /// that still names a donor asset.</summary>
    private static void RemapReferences(UAsset asset, IEnumerable<PropertyData> properties,
                                        IReadOnlyList<AssetReferenceRemap> references)
    {
        foreach (var property in properties)
        {
            switch (property)
            {
                case SoftObjectPropertyData soft:
                    RemapSoft(asset, soft, references);
                    break;
                case ArrayPropertyData array when array.Value is not null:
                    RemapReferences(asset, array.Value, references);
                    break;
                case StructPropertyData str when str.Value is not null:
                    RemapReferences(asset, str.Value, references);
                    break;
            }
        }
    }

    private static void RemapSoft(UAsset asset, SoftObjectPropertyData soft,
                                  IReadOnlyList<AssetReferenceRemap> references)
    {
        // The whole object path lives in the path's asset name.
        var current = soft.Value.AssetPath.AssetName?.ToString();
        if (string.IsNullOrEmpty(current))
            return;

        foreach (var reference in references)
        {
            if (reference.RemapPath(current) is not string rewritten)
                continue;

            var value = soft.Value;                                  // FSoftObjectPath is a struct: change a copy
            value.AssetPath.AssetName = FName.FromString(asset, rewritten);
            soft.Value = value;
            return;
        }
    }
}
