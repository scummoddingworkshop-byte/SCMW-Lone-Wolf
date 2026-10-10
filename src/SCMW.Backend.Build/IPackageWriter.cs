namespace SCMW.Backend.Build;

/// <summary>Applies recorded value changes to an extracted package (`.uasset` + paired `.uexp`).</summary>
public interface IPackageWriter
{
    WriteResult Apply(string uassetFilePath, IReadOnlyList<FieldChange> changes);
}
