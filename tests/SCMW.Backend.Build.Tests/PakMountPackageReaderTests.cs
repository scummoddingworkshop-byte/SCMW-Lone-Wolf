using SCMW.Backend.Build;
using SCMW.Backend.Pak;

namespace SCMW.Backend.Build.Tests;

public class PakMountPackageReaderTests
{
    [Fact]
    public void Reading_a_package_without_mounting_returns_false_never_throws()
    {
        using var mount = new PakMount();
        var reader = new PakMountPackageReader(mount);

        Assert.False(reader.TryReadPackage("SCUM/Content/Whatever", out var files));
        Assert.Null(files);
    }
}
