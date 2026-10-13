using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// Sets a link (an object reference) to point at another asset. The target is looked up in the
/// package's import table; when it is not there yet, a new import is added by cloning the current
/// link's class shape (so a crafting tag stays a crafting tag). This is the risky edit — callers
/// only offer it where the safety rule allows it (an ingredient's accepted tags).
/// </summary>
internal static class ObjectLink
{
    internal static bool TrySet(UAsset asset, ObjectPropertyData link, string raw, out string? error)
    {
        error = null;

        var target = raw?.Trim() ?? string.Empty;
        if (target.Length == 0)
        {
            error = "Enter the name of a game asset.";
            return false;
        }

        try
        {
            var name = FName.FromString(asset, target);

            // Already imported somewhere in this package: point at that import.
            var existing = asset.SearchForImport(name);
            if (existing >= 0)
            {
                link.Value = FPackageIndex.FromImport(existing);
                return true;
            }

            // Not imported yet: clone the current link's class shape, pointing at the new name.
            if (link.Value is null || !link.Value.IsImport())
            {
                error = "That link could not be changed safely.";
                return false;
            }

            var original = link.Value.ToImport(asset);
            link.Value = asset.AddImport(new Import(
                original.ClassPackage.ToString(), original.ClassName.ToString(), original.OuterIndex,
                target, original.bImportOptional, asset));
            return true;
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Changing a link failed ({ex.GetType().Name}).");
            error = "That link could not be changed.";
            return false;
        }
    }
}
