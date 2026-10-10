using SCMW.Core.Settings;

namespace SCMW.App;

/// <summary>
/// A settings store that keeps everything in memory — the safe default for tests and for
/// anywhere the app must not touch the disk.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private AppSettings _settings = new(null, null);

    public AppSettings Load() => _settings;

    public void Save(AppSettings s) => _settings = s;
}
