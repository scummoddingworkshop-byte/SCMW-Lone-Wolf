using SCMW.Backend.Pak;

namespace SCMW.Backend.Build;

/// <summary>The real reader: pulls package bytes straight out of the mounted SCUM paks.</summary>
public sealed class PakMountPackageReader : IPackageReader
{
    private readonly PakMount _mount;

    public PakMountPackageReader(PakMount mount) => _mount = mount;

    public bool TryReadPackage(string packagePath, out IReadOnlyDictionary<string, byte[]>? files)
        => _mount.TryReadPackage(packagePath, out files);
}
