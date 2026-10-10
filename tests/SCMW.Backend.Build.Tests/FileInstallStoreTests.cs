using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

public class FileInstallStoreTests
{
    [Fact]
    public void A_saved_record_round_trips_and_clears()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-store-" + Guid.NewGuid().ToString("N"));
        var store = new FileInstallStore(root);
        var record = new InstallRecord("p1", "Tape", "1.0.0", "SCMW-Tape_v1.0.0_P.pak",
            @"C:\b\SCMW-Tape_v1.0.0_P.pak", @"C:\g\~mods\SCMW-Tape_v1.0.0_P.pak",
            DateTime.UtcNow, null);
        try
        {
            Assert.Null(store.Load("p1"));
            store.Save(record);
            Assert.Equal(record, store.Load("p1"));
            store.Clear("p1");
            Assert.Null(store.Load("p1"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void A_corrupt_record_reads_as_none_not_a_crash()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "p1"));
        File.WriteAllText(Path.Combine(root, "p1", "installed.json"), "{ not json");
        try { Assert.Null(new FileInstallStore(root).Load("p1")); }
        finally { Directory.Delete(root, true); }
    }
}
