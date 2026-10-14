namespace SCMW.Backend.Build;

/// <summary>Applies recorded value changes to an extracted package (`.uasset` + paired `.uexp`).</summary>
public interface IPackageWriter
{
    /// <summary>
    /// Applies the changes to the package copy at <paramref name="uassetFilePath"/>.
    /// <paramref name="donorUassets"/> supplies, for an "add a field" change, the already-extracted
    /// `.uasset` of a game item that has the field (keyed by the field's own name); it is null or
    /// empty when no change adds a field.
    /// </summary>
    WriteResult Apply(string uassetFilePath, IReadOnlyList<FieldChange> changes,
                      IReadOnlyDictionary<string, string>? donorUassets = null);
}
