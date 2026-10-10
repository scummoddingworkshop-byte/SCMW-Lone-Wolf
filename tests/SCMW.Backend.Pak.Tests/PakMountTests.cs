// tests/SCMW.Backend.Pak.Tests/PakMountTests.cs
using System.Reflection;
using SCMW.Backend.Pak;
using SCMW.Core.Logging;
using Xunit;

public class PakMountTests
{
    /// <summary>
    /// Reads the curated constant from the production assembly instead of
    /// duplicating its text, so this test cannot drift from the message the
    /// product actually shows. <c>MountFailedMessage</c> is internal and there
    /// is no InternalsVisibleTo, hence reflection.
    /// </summary>
    private static string CuratedMountFailedMessage()
    {
        var field = typeof(PakMount).GetField(
            "MountFailedMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (string)field!.GetRawConstantValue()!;
    }

    [Fact]
    public void TryMount_malformed_aes_key_fails_with_curated_message()
    {
        // Real, existing directory so the missing-directory short-circuit does
        // not apply and the call reaches the try body: the malformed key makes
        // new FAesKey(...) throw, and only the real catch in PakMount.TryMount
        // turns that into the curated message. Fake-based tests cannot reach
        // this line, which is why they stay green when it regresses.
        var dir = Directory.CreateTempSubdirectory("scmw-pakmount-");
        try
        {
            using var mount = new PakMount();

            var ok = mount.TryMount(dir.FullName, aesKey: "not-valid-hex", out var error);

            Assert.False(ok);
            Assert.NotNull(error);
            Assert.Equal(CuratedMountFailedMessage(), error);
            // Belt-and-braces guards: plain English, and no echo of the key or
            // exception type text. Reverting `error = MountFailedMessage` to
            // `error = ex.Message` fails the Equal/Contains checks above
            // (observed: "Aes Key must be 32 bytes long").
            Assert.Contains("could not be read", error);
            Assert.DoesNotContain("not-valid-hex", error);
            Assert.DoesNotContain("FormatException", error);
            Assert.DoesNotContain("Exception", error);
            Assert.DoesNotContain('\n', error);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); }
            catch (IOException) { } // best-effort cleanup of the temp dir
        }
    }

    [Fact] // Final review: the mount-failure log must scrub the key in any case and without the 0x prefix
    public void Describe_scrubs_key_case_insensitively_and_without_prefix()
    {
        // Describe is internal and there is no InternalsVisibleTo, hence reflection.
        var describe = typeof(PakMount).GetMethod(
            "Describe", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(describe);

        const string body = "SECRET1234567890ABCDEF";
        var ex = new InvalidOperationException(
            "boom 0xsecret1234567890abcdef and secret1234567890abcdef");

        var described = (string)describe!.Invoke(null, new object?[] { ex, "0x" + body })!;

        Assert.DoesNotContain(body, described, StringComparison.OrdinalIgnoreCase); // key body never survives
        Assert.Contains("[redacted]", described);                                    // ...replaced
        Assert.Contains("InvalidOperationException", described);                     // type name kept
    }

    [Fact] // Final review: the mount-failure log must not carry the raw PAK path (folder name only)
    public void TryMount_failure_log_sanitizes_the_path_to_the_folder_name()
    {
        var logsDir = Path.Combine(Path.GetTempPath(), "scmw-mountlog-" + Guid.NewGuid());
        Log.SetLogsDir(logsDir);

        // A real directory with a recognizable leaf; the malformed key makes FAesKey
        // throw, which is exactly the path that logs the mount failure.
        var dir = Directory.CreateTempSubdirectory("scmw-pakmount-log-");
        try
        {
            using var mount = new PakMount();
            mount.TryMount(dir.FullName, aesKey: "not-valid-hex", out _);

            var logText = File.ReadAllText(Directory.GetFiles(logsDir)[0]);
            Assert.DoesNotContain(dir.FullName, logText); // raw path never logged
            Assert.Contains(dir.Name, logText);           // ...only the folder name
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); }
            catch (IOException) { } // best-effort cleanup of the temp dir
        }
    }
}
