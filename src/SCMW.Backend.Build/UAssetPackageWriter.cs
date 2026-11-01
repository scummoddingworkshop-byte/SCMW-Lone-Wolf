using SCMW.Core.Content;
using SCMW.Core.Editing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
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

        // What one build remembers: the entry shape of a list the project itself emptied, so an add
        // that comes after the removals still has something to copy (keyed by the list object).
        var emptiedLists = new Dictionary<object, PropertyData>(ReferenceEqualityComparer.Instance);

        foreach (var change in changes)
        {
            // A list change adds or removes an entry rather than setting a value.
            if (change.Operation is ChangeOperation.AddEntry or ChangeOperation.RemoveEntry)
            {
                var applied = false;
                foreach (var candidate in exports)
                {
                    if (TryApplyListChange(candidate.Data, change, out var found, out var listError,
                                           emptiedLists))
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
                ? SetLink(asset, link, change.NewValue, out error)
                : target is SoftObjectPropertyData soft
                    ? ObjectLink.TrySetSoft(asset, soft, change.NewValue, out error)
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
    /// Points an object link at a new target. A bare name keeps the link's own folder (the game's
    /// ingredient tags), while a whole path (<c>SCUM/Content/…</c> or <c>/Game/…</c>) is used exactly
    /// as given — that is how a link can point at a category the project itself created.
    /// </summary>
    private static bool SetLink(UAsset asset, ObjectPropertyData link, string raw, out string? error)
    {
        error = null;
        var text = (raw ?? string.Empty).Trim();

        if (text.Contains('/') && link.Value is { } value && value.IsImport())
            return ObjectLink.TrySetNew(asset, link, text, value.ToImport(asset), out error);

        return ObjectLink.TrySet(asset, link, text, out error);
    }

    /// <summary>
    /// Adds or removes one entry of the list a list-change names. <paramref name="found"/> is true
    /// when the list was found here (so a caller can tell "not in this export" from a real failure).
    /// </summary>
    /// <param name="emptiedLists">
    /// What this build remembers between changes: the entry shape of a list the project's own removals
    /// emptied, keyed by the list itself. A project can remove an item's last category and then add a
    /// new one, and the add still needs an entry to copy — this is where it comes from. Null for a
    /// one-off change, where an empty list has nothing to copy.
    /// </param>
    internal static bool TryApplyListChange(
        IReadOnlyList<PropertyData> properties, FieldChange change, out bool found, out string? error,
        IDictionary<object, PropertyData>? emptiedLists = null)
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
                // The list is empty because this project removed its entries a moment ago: the shape
                // of the last one removed is remembered, so the new entry can still be built.
                if (emptiedLists is null || !emptiedLists.TryGetValue(array, out var remembered))
                {
                    error = "There is nothing in this list to copy for the new entry — remove that change in Projects, "
                            + "then add it again from a list that has something in it.";
                    return false;
                }

                array.Value = new[] { DeepClone(remembered) };
                return true;
            }
            if (array.Value.Length >= MaxListEntries)
            {
                error = $"\"{change.Field}\" already has as many entries as the mod maker allows.";
                return false;
            }

            var grown = array.Value.ToList();
            grown.Add(DeepClone(array.Value[^1]));
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
        var removed = kept[index];
        kept.RemoveAt(index);
        array.Value = kept.ToArray();

        // Remember the shape of the entry that emptied the list, for an add that may follow.
        if (kept.Count == 0 && emptiedLists is not null)
            emptiedLists[array] = DeepClone(removed);

        return true;
    }

    /// <summary>
    /// A deep copy of one list entry. UAssetAPI's <c>Clone()</c> gives a new entry but keeps the
    /// <i>nested</i> property objects shared with the original, so an edit to the copy (say a new
    /// slot's tag) would silently rewrite the entry it was copied from. This copies every level of a
    /// struct or list, so the new entry stands on its own.
    /// </summary>
    private static PropertyData DeepClone(PropertyData property)
    {
        var copy = (PropertyData)property.Clone()!;
        switch (copy)
        {
            case StructPropertyData structure:
                for (var i = 0; i < structure.Value.Count; i++)
                    structure.Value[i] = DeepClone(structure.Value[i]);
                break;

            case ArrayPropertyData array:
                for (var i = 0; i < array.Value.Length; i++)
                    array.Value[i] = DeepClone(array.Value[i]);
                break;
        }

        return copy;
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

        // A list of tagged entries (an item's "what I count as" list) is its own small shape: one
        // entry, copied from the donor, whose link points at the category the owner chose.
        if (shape is ArrayPropertyData tagged)
            return TryInsertClonedTaggedList(target.Data, tagged, donor, change.Field, change.NewValue,
                                             asset, out error);

        return TryInsertClonedField(target.Data, shape, change.Field, change.NewValue, asset, out error);
    }

    /// <summary>
    /// Adds a list of tagged entries — the shape an item uses to say which ingredient categories it
    /// counts as (<c>CraftingIngredientInstances</c>): one entry holding a link and an amount. The
    /// entry's shape (struct type and link class) is copied from the donor; the link is pointed at
    /// <paramref name="targetPath"/>'s category in this package's own import table; the amount is 1.
    /// Refuses plainly when the donor is not that shape, so a guessed list can never break the pak.
    /// </summary>
    internal static bool TryInsertClonedTaggedList(
        List<PropertyData> targetProperties, ArrayPropertyData donor, UAsset donorAsset, string field,
        string targetPath, UAsset targetAsset, out string? error)
    {
        error = null;

        var names = targetProperties.Select(p => p.Name.ToString()).ToList();
        if (FindPropertyIndex(names, field) >= 0)
        {
            error = $"\"{field}\" is already in this file — change its value instead of adding it.";
            return false;
        }

        if (donor.Value is not { Length: > 0 } || donor.Value[0] is not StructPropertyData donorEntry)
        {
            error = $"\"{field}\" is not a list we can add.";
            return false;
        }

        var donorLink = donorEntry.Value.OfType<ObjectPropertyData>().FirstOrDefault();
        var donorAmount = donorEntry.Value.OfType<FloatPropertyData>().FirstOrDefault();
        var structType = donorEntry.StructType.ToString();
        var arrayType = donor.ArrayType.ToString();

        if (donorLink?.Value is null || !donorLink.Value.IsImport()
            || donorAmount is null || string.IsNullOrEmpty(structType) || string.IsNullOrEmpty(arrayType))
        {
            error = $"\"{field}\" is not a list of tagged entries.";
            return false;
        }

        var donorShape = donorLink.Value.ToImport(donorAsset);
        var linkField = donorLink.Name.ToString();
        var amountField = donorAmount.Name.ToString();

        var amount = new FloatPropertyData(FName.FromString(targetAsset, amountField)) { Value = 1f };
        var link = new ObjectPropertyData(FName.FromString(targetAsset, linkField));
        if (!ObjectLink.TrySetNew(targetAsset, link, targetPath, donorShape, out error))
            return false;

        var entry = new StructPropertyData(FName.FromString(targetAsset, field),
                                           FName.FromString(targetAsset, structType))
        {
            Value = new List<PropertyData> { link, amount },
        };

        targetProperties.Add(new ArrayPropertyData(FName.FromString(targetAsset, field))
        {
            ArrayType = FName.FromString(targetAsset, arrayType),
            Value = new PropertyData[] { entry },
        });
        return true;
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
