using SCMW.Core.Content;
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

    public WriteResult Apply(string uassetFilePath, IReadOnlyList<FieldChange> changes,
                             IReadOnlyDictionary<string, string>? donorUassets = null)
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

        // Every failure names the item file and the value it was working on, so even a plain reason
        // says where to look.
        WriteResult Failed(string reason, string field) =>
            new(false, $"{reason} (item \"{assetName}\", value \"{FieldNames.Friendly(field)}\")");

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
                        return Failed(listError ?? "That list could not be changed.", change.Field);
                }

                if (!applied)
                    return Failed($"\"{change.Field}\" was not found in this item.", change.Field);
                continue;
            }

            // A whole new field: its exact shape is copied from a donor item that already has it.
            if (change.Operation == ChangeOperation.AddField)
            {
                if (!TryApplyAddField(asset, exports, change, donorUassets, out var addError))
                    return Failed(addError ?? "That field could not be added.", change.Field);
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
                return Failed($"The value \"{change.Field}\" was not found in this item.", change.Field);

            string? error;
            var ok = target is ObjectPropertyData link
                ? ObjectLink.TrySet(asset, link, change.NewValue, out error)
                : PropertyValue.TrySet(target, asset, change.NewValue, out error);

            if (!ok)
                return Failed(error ?? "That value couldn't be set.", change.Field);
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
    /// Adds a whole new field to the target: opens the donor item that already has it, copies the
    /// property's exact shape, sets the value, and inserts it into the matching target export (the
    /// same object the donor kept it on, else the class default object). Fails plainly when there is
    /// no donor file or no such field in it — never a guessed shape.
    /// </summary>
    private static bool TryApplyAddField(
        UAsset asset, List<NormalExport> exports, FieldChange change,
        IReadOnlyDictionary<string, string>? donorUassets, out string? error)
    {
        error = null;

        if (donorUassets is null || !donorUassets.TryGetValue(change.Field, out var donorPath)
            || string.IsNullOrEmpty(donorPath))
        {
            error = $"The game file that has \"{change.Field}\" to copy from was not found.";
            return false;
        }

        UAsset donor;
        try
        {
            donor = new UAsset(donorPath, EngineVersion.VER_UE4_27);
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Opening the donor file for an added field failed ({ex.GetType().Name}).");
            error = "The game file to copy the new field from could not be opened.";
            return false;
        }

        var donorCdoName = $"Default__{Path.GetFileNameWithoutExtension(donorPath)}_C";
        var donorExports = donor.Exports.OfType<NormalExport>()
            .OrderByDescending(e => e.ObjectName.ToString() == donorCdoName)
            .ToList();

        PropertyData? shape = null;
        string? holderName = null;
        foreach (var candidate in donorExports)
        {
            shape = PropertyPath.Resolve(candidate.Data, change.Field);
            if (shape is not null)
            {
                holderName = candidate.ObjectName.ToString();
                break;
            }
        }

        if (shape is null)
        {
            error = $"\"{change.Field}\" was not found in the file we copy it from.";
            return false;
        }

        // Put the field back where the donor kept it — its own object, or the same component — and
        // fall back to the class default object when the target has no export going by that name.
        var target = exports.FirstOrDefault(e => e.ObjectName.ToString() == holderName) ?? exports[0];

        return TryInsertClonedField(target.Data, shape, change.Field, change.NewValue, asset, out error);
    }

    /// <summary>
    /// Copies a donor property's exact shape (its UE type) under <paramref name="field"/>, sets the
    /// value on the copy, and adds it to the target export's property list. Refuses plainly — adding
    /// nothing — when the target already has the field, when the value does not fit the shape, or when
    /// the shape is a kind we cannot write; a guessed shape would break the "pak must load" guard.
    /// </summary>
    internal static bool TryInsertClonedField(
        List<PropertyData> targetProperties, PropertyData donor, string field, string newValue,
        INameMap targetNames, out string? error)
    {
        error = null;

        var names = targetProperties.Select(p => p.Name.ToString()).ToList();
        if (FindPropertyIndex(names, field) >= 0)
        {
            error = $"\"{field}\" is already in this file — change its value instead of adding it.";
            return false;
        }

        if (donor.Clone() is not PropertyData clone)
        {
            error = $"\"{field}\" could not be copied.";
            return false;
        }

        // The copy keeps the donor's exact type, but its name is re-taken from the target's own name
        // map (the donor's name index means nothing in the target package).
        clone.Name = FName.FromString(targetNames, field);

        switch (clone)
        {
            // The simple value kinds, and a plain byte. Richer shapes (structs, lists, object links,
            // enums) borrow a shape that needs more than a name to travel — left for later.
            case FloatPropertyData or DoublePropertyData or IntPropertyData or Int64PropertyData
                 or BoolPropertyData or StrPropertyData or TextPropertyData or NamePropertyData:
                break;
            case BytePropertyData b when b.ByteType == BytePropertyType.Byte:
                break;
            default:
                error = $"\"{field}\" is a kind of value the mod maker cannot add yet.";
                return false;
        }

        if (!PropertyValue.TrySet(clone, targetNames, newValue, out error))
            return false;

        targetProperties.Add(clone);
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
