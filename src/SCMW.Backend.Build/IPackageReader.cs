namespace SCMW.Backend.Build;

/// <summary>Reads one cooked package's raw files (`.uasset`/`.uexp`/…) out of the mounted game.</summary>
public interface IPackageReader
{
    bool TryReadPackage(string packagePath, out IReadOnlyDictionary<string, byte[]>? files);

    /// <summary>
    /// Reads the game's own asset index (<c>AssetRegistry.bin</c>) — not a package, so it cannot go
    /// through <see cref="TryReadPackage"/>. Default returns false so simple fakes need not implement
    /// it.
    /// </summary>
    bool TryReadAssetIndex(out byte[]? bytes)
    {
        bytes = null;
        return false;
    }
}
