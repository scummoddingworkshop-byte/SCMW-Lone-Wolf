namespace SCMW.Core.Editing;

/// <summary>
/// Works out the value a field should display once a project's change records are applied on top of
/// the game's current value. The <em>latest</em> (last-listed) record for an asset+field wins;
/// field names are compared case-insensitively so a hand-edited <c>changes.json</c> is not silently
/// ignored. When no record touches the field the supplied game value is returned unchanged.
/// </summary>
public static class AppliedValue
{
    /// <summary>
    /// The applied value for <paramref name="assetId"/>+<paramref name="field"/> — the newest
    /// matching record's <see cref="ChangeRecord.NewValue"/>, or <paramref name="gameValue"/> when
    /// the project has no record for that field.
    /// </summary>
    public static string? Resolve(
        IReadOnlyList<ChangeRecord> records, string assetId, string field, string? gameValue)
    {
        for (var i = records.Count - 1; i >= 0; i--)
        {
            var record = records[i];

            // A list change (add/remove an entry) is not a value; it must never be read as one.
            if (record.Operation != ChangeOperation.Set) continue;

            if (string.Equals(record.AssetId, assetId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.FieldTechnicalName, field, StringComparison.OrdinalIgnoreCase))
            {
                return record.NewValue;
            }
        }

        return gameValue;
    }

    /// <summary>
    /// The applied value for a file addressed by its own PATH: the newest
    /// <see cref="ChangeOperation.Set"/> record whose <see cref="ChangeRecord.AssetPath"/> matches
    /// <paramref name="assetPath"/> (case-insensitively), else <paramref name="gameValue"/>. A record
    /// with no path never matches — path-addressed means path, so two files that share a display name
    /// can never be confused.
    /// </summary>
    public static string? ResolveForPath(
        IReadOnlyList<ChangeRecord> records, string assetPath, string field, string? gameValue)
    {
        if (string.IsNullOrEmpty(assetPath)) return gameValue;

        for (var i = records.Count - 1; i >= 0; i--)
        {
            var record = records[i];

            // A list change (add/remove an entry) is not a value; it must never be read as one.
            if (record.Operation != ChangeOperation.Set && record.Operation != ChangeOperation.Multiply)
                continue;

            if (string.IsNullOrEmpty(record.AssetPath)
                || !string.Equals(record.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(record.FieldTechnicalName, field, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The newest value change wins. A set is an absolute value; a multiply scales the file's
            // own value by its factor (so it always works from the game's number, never an earlier edit).
            if (record.Operation == ChangeOperation.Set) return record.NewValue;

            return Multiplier.TryParse(record.NewValue, out var factor)
                ? Multiplier.Apply(gameValue, factor)
                : gameValue;
        }

        return gameValue;
    }

    /// <summary>
    /// The value a field should show for an asset addressed first by its PATH and, only when it has
    /// no path, by its name. This is the one home for "which change belongs to this file": a file
    /// with a path is matched by path alone (so two files that share a name never see each other's
    /// changes), and a sample item with no file falls back to its name.
    /// </summary>
    public static string? ResolveForAsset(
        IReadOnlyList<ChangeRecord> records, string? assetPath, string assetName, string field, string? gameValue)
        => string.IsNullOrEmpty(assetPath)
            ? Resolve(records, assetName, field, gameValue)
            : ResolveForPath(records, assetPath, field, gameValue);

    /// <summary>
    /// The value an asset's field had before the project's first change to it, matched the same way
    /// as <see cref="ResolveForAsset"/> (path first, name only when there is no path).
    /// </summary>
    public static string? OriginalForAsset(
        IReadOnlyList<ChangeRecord> records, string? assetPath, string assetName, string field)
    {
        var byPath = !string.IsNullOrEmpty(assetPath);

        foreach (var record in records)
        {
            // Only a value change carries a baseline; a list change does not.
            if (record.Operation != ChangeOperation.Set && record.Operation != ChangeOperation.Multiply)
                continue;

            var assetMatches = byPath
                ? string.Equals(record.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase)
                : string.Equals(record.AssetId, assetName, StringComparison.OrdinalIgnoreCase);

            if (assetMatches
                && string.Equals(record.FieldTechnicalName, field, StringComparison.OrdinalIgnoreCase))
            {
                return record.OldValue;
            }
        }

        return null;
    }
}
