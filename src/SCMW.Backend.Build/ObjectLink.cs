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
            // SearchForImport returns the raw package index (negative for an import, 0 for none).
            var existing = asset.SearchForImport(name);
            if (existing != 0)
            {
                link.Value = FPackageIndex.FromRawIndex(existing);
                return true;
            }

            // Not imported yet: a tag lives in its own package (named after the tag), and the tag
            // import's outer must point at that package — the game resolves the whole path, not just
            // the name. Copy the current link's class shape, but give the copy its own package,
            // mirroring the tag imports the game's own files already use.
            if (link.Value is null || !link.Value.IsImport())
            {
                error = "That link could not be changed safely.";
                return false;
            }

            var original = link.Value.ToImport(asset);
            if (original?.OuterIndex?.IsImport() != true)
            {
                error = "That link could not be changed safely.";
                return false;
            }

            var originalPackage = original.OuterIndex.ToImport(asset);
            if (originalPackage is null)
            {
                error = "That link could not be changed safely.";
                return false;
            }

            var packageLink = FindOrAddImport(asset, originalPackage, ReplaceLastSegment(originalPackage.ObjectName.ToString(), target));
            link.Value = asset.AddImport(new Import(
                original.ClassPackage.ToString(), original.ClassName.ToString(), packageLink,
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

    /// <summary>
    /// Sets a "soft" link (a cookable food item) to point at another asset. The game stores the
    /// whole object path in the path's asset name (its package name is left empty), so the new
    /// path is written the same way.
    /// </summary>
    internal static bool TrySetSoft(UAsset asset, SoftObjectPropertyData link, string raw, out string? error)
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
            var value = link.Value;              // FSoftObjectPath is a struct: change a copy, then set it back
            value.AssetPath.AssetName = name;    // the whole object path lives here
            link.Value = value;
            return true;
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Changing a soft link failed ({ex.GetType().Name}).");
            error = "That link could not be changed.";
            return false;
        }
    }

    /// <summary>
    /// Points a link at a category (a <c>CI_*</c> asset) whose package does not exist in this file yet.
    /// Used when a whole new list entry is added from a donor: there is no existing link to copy the
    /// package from, so the donor's tag import supplies the class shape and the target path supplies
    /// the package. The game stores one package import per tag, named with the tag's full path.
    /// </summary>
    internal static bool TrySetNew(UAsset asset, ObjectPropertyData link, string targetPath,
                                   Import? donorShape, out string? error)
    {
        error = null;

        var path = GamePath(targetPath);
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        if (path.Length == 0 || name.Length == 0 || donorShape is null)
        {
            error = "That link could not be changed safely.";
            return false;
        }

        try
        {
            // Already imported somewhere in this package: point at that import.
            var existing = asset.SearchForImport(FName.FromString(asset, name));
            if (existing != 0)
            {
                link.Value = FPackageIndex.FromRawIndex(existing);
                return true;
            }

            var package = asset.AddImport(new Import(
                "/Script/CoreUObject", "Package", FPackageIndex.FromRawIndex(0), path, false, asset));
            link.Value = asset.AddImport(new Import(
                donorShape.ClassPackage.ToString(), donorShape.ClassName.ToString(), package, name,
                donorShape.bImportOptional, asset));
            return true;
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Changing a link failed ({ex.GetType().Name}).");
            error = "That link could not be changed safely.";
            return false;
        }
    }

    /// <summary>
    /// The <c>/Game/…</c> form of an asset path: the game's index and imports use it, while a project
    /// folder and the pak use <c>SCUM/Content/…</c>. A path already in game form is kept as it is.
    /// </summary>
    internal static string GamePath(string? path)
    {
        var text = (path ?? string.Empty).Trim().Replace('\\', '/');
        const string pakPrefix = "SCUM/Content/";
        if (text.StartsWith(pakPrefix, StringComparison.OrdinalIgnoreCase))
            return "/Game/" + text[pakPrefix.Length..];
        return text.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) ? text : string.Empty;
    }

    /// <summary>The import for <paramref name="name"/>, adding one shaped like <paramref name="shape"/>
    /// when the package does not reference it yet.</summary>
    private static FPackageIndex FindOrAddImport(UAsset asset, Import shape, string name)
    {
        var found = asset.SearchForImport(FName.FromString(asset, name));
        return found != 0
            ? FPackageIndex.FromRawIndex(found)
            : asset.AddImport(new Import(shape.ClassPackage.ToString(), shape.ClassName.ToString(),
                                         shape.OuterIndex, name, shape.bImportOptional, asset));
    }

    /// <summary>A path with its last segment swapped: the new tag's own package name.</summary>
    private static string ReplaceLastSegment(string path, string last)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[..(slash + 1)] + last : last;
    }
}
