using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// The proven writer (UAssetAPI, UE4.27) editing a package copy. Values are set on the item's
/// class default object (`Default__&lt;AssetName&gt;_C`). A value type we cannot set safely fails the
/// whole build with one plain reason — never a half-changed asset.
/// </summary>
public sealed class UAssetPackageWriter : IPackageWriter
{
    public WriteResult Apply(string uassetFilePath, IReadOnlyList<FieldChange> changes)
    {
        UAsset asset;
        try
        {
            asset = new UAsset(uassetFilePath, EngineVersion.VER_UE4_27);
        }
        catch (Exception ex)
        {
            return new WriteResult(false, "The game file could not be opened for editing (" + ex.GetType().Name + ").");
        }

        var assetName = Path.GetFileNameWithoutExtension(uassetFilePath);
        var cdoName = $"Default__{assetName}_C";

        // A field can live on the item's own object OR on a component object inside the package
        // (e.g. MaxQuantity on a stackable component), so search every export — the CDO first.
        var exports = asset.Exports.OfType<NormalExport>()
            .OrderByDescending(e => e.ObjectName.ToString() == cdoName)
            .ToList();
        if (exports.Count == 0)
            return new WriteResult(false, "This item's editable data was not in the shape we expected.");

        foreach (var change in changes)
        {
            NormalExport? holder = null;
            var index = -1;
            foreach (var candidate in exports)
            {
                index = FindPropertyIndex(candidate.Data.Select(p => p.Name.ToString()).ToList(), change.Field);
                if (index >= 0) { holder = candidate; break; }
            }

            if (holder is null)
                return new WriteResult(false, $"The value \"{change.Field}\" was not found in this item.");

            if (!PropertyValue.TrySet(holder.Data[index], asset, change.NewValue, out var error))
                return new WriteResult(false, error);
        }

        try
        {
            asset.Write(uassetFilePath);
        }
        catch (Exception ex)
        {
            return new WriteResult(false, "The changed game file could not be saved (" + ex.GetType().Name + ").");
        }

        return new WriteResult(true, null);
    }

    /// <summary>
    /// Finds a property by the game's own name, or — for edits recorded before the technical name
    /// was kept — by its friendly label (e.g. "Inventory Height"). Returns -1 when neither matches.
    /// </summary>
    internal static int FindPropertyIndex(IReadOnlyList<string> propertyNames, string field)
    {
        for (var i = 0; i < propertyNames.Count; i++)
            if (string.Equals(propertyNames[i], field, StringComparison.OrdinalIgnoreCase)) return i;

        for (var i = 0; i < propertyNames.Count; i++)
            if (string.Equals(SCMW.Core.Content.FieldNames.Friendly(propertyNames[i]), field, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }
}
