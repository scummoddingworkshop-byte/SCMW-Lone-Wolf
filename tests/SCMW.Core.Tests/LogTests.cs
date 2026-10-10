using SCMW.Core.Logging;
using Xunit;

public class LogTests
{
    [Fact] // Step 4: one entry written -> file contains text
    public void Write_one_entry_file_contains_text()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-log-" + Guid.NewGuid());
        Log.SetLogsDir(dir);
        Log.Info("log smoke entry");
        var logText = File.ReadAllText(Directory.GetFiles(dir)[0]);
        Assert.Contains("log smoke entry", logText);
        Assert.Contains("[INF]", logText); // template: level present
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", logText); // template: timestamp present
    }

    [Fact] // Fix round 1: concurrent writes must all land, none silently lost
    public void Concurrent_writes_are_not_lost()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-log-conc-" + Guid.NewGuid());
        Log.SetLogsDir(dir);
        Parallel.For(0, 50, i => Log.Warn("concurrent-marker-" + i));
        var text = string.Concat(Directory.GetFiles(dir).Select(File.ReadAllText));
        var found = text.Split('\n').Count(l => l.Contains("concurrent-marker-"));
        Assert.Equal(50, found);
    }

    [Fact] // Final review: redaction must be case-insensitive and prefix-tolerant
    public void Redaction_is_case_insensitive_and_prefix_tolerant()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-log-redact-" + Guid.NewGuid());
        Log.SetLogsDir(dir);

        const string body = "SECRET1234567890ABCDEF";
        Log.RegisterSecret("0x" + body);

        var swapped = "0xsecret1234567890abcdef";  // same key, different letter case
        var bare = "secret1234567890abcdef";       // same key, no 0x prefix (and swapped case)
        Log.Info("swapped: " + swapped + " bare: " + bare + " exact: 0x" + body);

        var logText = File.ReadAllText(Directory.GetFiles(dir)[0]);
        Assert.DoesNotContain(body, logText, StringComparison.OrdinalIgnoreCase); // no key body in any case
        Assert.Contains("[redacted]", logText);                                   // ...replaced by the placeholder
    }
}
