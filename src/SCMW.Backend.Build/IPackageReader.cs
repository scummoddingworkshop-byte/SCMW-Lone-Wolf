namespace SCMW.Backend.Build;

/// <summary>Reads one cooked package's raw files (`.uasset`/`.uexp`/…) out of the mounted game.</summary>
public interface IPackageReader
{
    bool TryReadPackage(string packagePath, out IReadOnlyDictionary<string, byte[]>? files);
}
