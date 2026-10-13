using SCMW.Core.Logging;
using SCMW.Core.Settings;
using Xunit;

public class SettingsStoreTests
{
    [Fact]
    public void RoundTrip_preserves_paths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-set-" + Guid.NewGuid());
        var store = new JsonSettingsStore(dir);
        var s = new AppSettings(@"D:\SCMW\Workspace 游", @"D:\Games\SCUM", "125621");
        store.Save(s);
        Assert.Equal(s, store.Load());
    }

    [Fact] // Task 1 (Plan 3): the active project id persists like every other setting
    public void ActiveProjectId_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-set-" + Guid.NewGuid());
        var store = new JsonSettingsStore(dir);

        store.Save(new AppSettings(null, null, ActiveProjectId: "abc12345"));

        Assert.Equal("abc12345", store.Load().ActiveProjectId);
    }

    [Fact] // v1.0.1: the settings write is atomic (temp file, then swap) and leaves no temp behind
    public void Save_writes_atomically_and_leaves_no_temp_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-set-" + Guid.NewGuid());
        var store = new JsonSettingsStore(dir);

        store.Save(new AppSettings(@"C:\w", @"C:\g"));
        store.Save(new AppSettings(@"C:\w2", @"C:\g2"));   // a second write replaces cleanly

        Assert.Equal(@"C:\w2", store.Load().WorkspacePath!.TrimEnd('\\'));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact] // a locked settings file must not crash the app on save (it saves on every keystroke)
    public void A_locked_settings_file_does_not_throw_on_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-set-" + Guid.NewGuid());
        var store = new JsonSettingsStore(dir);
        store.Save(new AppSettings(@"C:\w", @"C:\g"));

        var path = Path.Combine(dir, "settings.json");
        using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Save(new AppSettings(@"C:\w2", @"C:\g2"));   // must not throw
        }

        Assert.Equal(@"C:\w", store.Load().WorkspacePath);     // the previous file is intact
    }

    [Fact] // Review Focus #3: corrupt settings -> defaults + warn, never crash
    public void Corrupt_json_falls_back_to_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-set-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ this is not json");
        var s = new JsonSettingsStore(dir).Load();
        Assert.NotNull(s);
        Assert.Null(s.WorkspacePath);
    }

    [Fact] // AES key: stored locally, but never present in logs
    public void AesKey_round_trips_and_is_never_logged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-aes-" + Guid.NewGuid());
        var logsDir = Path.Combine(dir, "logs");
        var store = new JsonSettingsStore(Path.Combine(dir, "settings"));
        var key = "0x0000000000000000000000000000000000000000000000000000000000000000";
        store.Save(new AppSettings(null, null, AesKey: key));
        Assert.Equal(key, store.Load().AesKey);

        Log.SetLogsDir(logsDir);
        Log.Warn("testing aes key leakage check: " + key);
        var logText = File.ReadAllText(Directory.GetFiles(logsDir)[0]);
        Assert.DoesNotContain(key, logText);            // Log.Warn scrubs AesKey-shaped strings
        Assert.Contains("testing aes key leakage check", logText); // message preserved
    }

    [Fact] // Task 7: RegisterSecret must scrub the key from Info-level messages too (not just Warn)
    public void RegisterSecret_scrubs_key_from_Info_messages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-aes-info-" + Guid.NewGuid());
        var logsDir = Path.Combine(dir, "logs");
        var key = "0xDUMMY0000000000000000000000000000000000000000000000000000000000";

        Log.SetLogsDir(logsDir);
        Log.RegisterSecret(key);
        Log.Info("registered key check: " + key);

        var logText = File.ReadAllText(Directory.GetFiles(logsDir)[0]);
        Assert.DoesNotContain(key, logText);                 // the key is scrubbed
        Assert.Contains("registered key check", logText);    // the message survives
        Assert.Contains("[redacted]", logText);              // ...as the redaction placeholder
    }
}
