using SCMW.Core.Editing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// The proven writer (UAssetAPI, UE4.27) editing a package copy. Values are set on the item's
/// class default object (`Default__&lt;AssetName&gt;_C`); a list change adds or removes an entry.
/// A kind we cannot write safely fails the whole build with one plain reason — never a half-changed asset.
/// </summary>
public sealed class UAssetPackageWriter : IPackageWriter
{
    /// <summary>The most entries one list may grow to in a single build, so a mistake cannot run away.</summary>
    private const int MaxListEntries = 32;

    public WriteResult Apply(string uassetFilePath, IReadOnlyList<FieldChange> changes)
    {
        UAsset asset;
        try
        {
            asset = new UAsset(uassetFilePath, EngineVersion.VER_UE4_27);
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Opening a game file for editing failed ({ex.GetType().Name}).");
            return new WriteResult(false, "The game file could not be opened for editing.");
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
            // A list change adds or removes an entry rather than setting a value.
            if (change.Operation is ChangeOperation.AddEntry or ChangeOperation.RemoveEntry)
            {
                var applied = false;
                foreach (var candidate in exports)
                {
                    if (TryApplyListChange(candidate.Data, change, out var found, out var listError))
                    {
                        applied = true;
                        break;
                    }

                    if (found)
                        return new WriteResult(false, listError);
                }

                if (!applied)
                    return new WriteResult(false, $"\"{change.Field}\" was not found in this item.");
                continue;
            }

            // A change names either a top-level property or a dotted path into a struct; the
            // resolver walks the path (and the exports, the CDO first) to the value to set.
            PropertyData? target = null;
            foreach (var candidate in exports)
            {
                target = PropertyPath.Resolve(candidate.Data, change.Field);
                if (target is not null) break;
            }

            if (target is null)
                return new WriteResult(false, $"The value \"{change.Field}\" was not found in this item.");

            string? error;
            var ok = target is ObjectPropertyData link
                ? ObjectLink.TrySet(asset, link, change.NewValue, out error)
                : PropertyValue.TrySet(target, asset, change.NewValue, out error);

            if (!ok)
                return new WriteResult(false, error);
        }

        try
        {
            asset.Write(uassetFilePath);
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Saving a changed game file failed ({ex.GetType().Name}).");
            return new WriteResult(false, "The changed game file could not be saved.");
        }

        return new WriteResult(true, null);
    }

    /// <summary>
    /// Adds or removes one entry of the list a list-change names. <paramref name="found"/> is true
    /// when the list was found here (so a caller can tell "not in this export" from a real failure).
    /// </summary>
    internal static bool TryApplyListChange(
        IReadOnlyList<PropertyData> properties, FieldChange change, out bool found, out string? error)
    {
        found = false;
        error = null;

        var array = PropertyPath.ResolveList(properties, change.Field, out var index);
        if (array is null) return false;
        found = true;

        if (change.Operation == ChangeOperation.AddEntry)
        {
            if (array.Value.Length == 0)
            {
                error = $"There is no existing entry of \"{change.Field}\" to copy for the new one.";
                return false;
            }
            if (array.Value.Length >= MaxListEntries)
            {
                error = $"\"{change.Field}\" already has as many entries as the mod maker allows.";
                return false;
            }

            var grown = array.Value.ToList();
            grown.Add((PropertyData)array.Value[^1].Clone()!);
            array.Value = grown.ToArray();
            return true;
        }

        // Remove the named entry.
        if (index < 0 || index >= array.Value.Length)
        {
            error = $"The entry to remove was not found in \"{change.Field}\".";
            return false;
        }

        var kept = array.Value.ToList();
        kept.RemoveAt(index);
        array.Value = kept.ToArray();
        return true;
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
