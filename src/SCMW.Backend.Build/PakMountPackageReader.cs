using SCMW.Backend.Pak;

namespace SCMW.Backend.Build;

/// <summary>The real reader: pulls package bytes straight out of the mounted SCUM paks.</summary>
public sealed class PakMountPackageReader : IPackageReader
{
    /// <summary>Where the game's own asset index normally sits inside the paks.</summary>
    public const string GameIndexPath = "SCUM/AssetRegistry.bin";

    private readonly PakMount _mount;

    public PakMountPackageReader(PakMount mount) => _mount = mount;

    public bool TryReadPackage(string packagePath, out IReadOnlyDictionary<string, byte[]>? files)
        => _mount.TryReadPackage(packagePath, out files);

    public bool TryReadAssetIndex(out byte[]? bytes)
    {
        // The index sits at a known path; fall back to finding it by name, in case a build moves it.
        if (_mount.TryReadFile(GameIndexPath, out bytes) && bytes is not null) return true;

        foreach (var path in _mount.FilePaths)
        {
            if (path.EndsWith("/AssetRegistry.bin", StringComparison.OrdinalIgnoreCase)
                && _mount.TryReadFile(path, out bytes) && bytes is not null)
                return true;
        }

        bytes = null;
        return false;
    }
}
