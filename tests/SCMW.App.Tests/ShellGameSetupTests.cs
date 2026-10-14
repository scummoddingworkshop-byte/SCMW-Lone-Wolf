using Avalonia.Headless.XUnit;
using SCMW.Core.Content;
using SCMW.Core.Settings;

namespace SCMW.App.Tests;

public class ShellGameSetupTests
{
    /// <summary>A content source that is always ready; counts reads so we can see a re-read.</summary>
    private sealed class ReadyContent : IContentSource
    {
        public int Reads;

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", null);
        public ScanInfo? LastScan => null;
        public IIconCache Icons { get; } = new NoIcons();
        public IReadOnlyList<string> Categories => Array.Empty<string>();
        public void Configure(string p, string? k, string? b) { }

        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default)
        { Reads++; return Task.FromResult(Status); }

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default)
        { Reads++; return Task.FromResult<IReadOnlyList<AssetSummary>>(Array.Empty<AssetSummary>()); }
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>An empty icon cache — the screens can talk to it without touching any disk.</summary>
    private sealed class NoIcons : IIconCache
    {
        public string IconsDirectory => "";
        public string? GetIconPath(string assetId) => null;
        public int Count => 0;
        public long TotalBytes => 0;
        public void Clear() { }
    }

    [AvaloniaFact]
    public void Setting_up_scum_mid_session_wakes_items_build_mods_and_launch()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-shell-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A stand-in "SCUM install" with its executable and a mod already in ~mods.
            var game = Path.Combine(root, "SCUM");
            Directory.CreateDirectory(Path.Combine(game, "SCUM", "Binaries", "Win64"));
            File.WriteAllText(Path.Combine(game, "SCUM", "Binaries", "Win64", "SCUM.exe"), "x");
            var mods = Path.Combine(game, "SCUM", "Content", "Paks", "~mods");
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(mods, "SomeMod.pak"), "x");

            var store = new InMemorySettingsStore();
            var content = new ReadyContent();
            var vm = new ShellViewModel(new NavigationHost(), settingsStore: store, contentSource: content);

            Assert.False(vm.CanLaunch);                       // nothing configured at startup
            var readsBefore = content.Reads;

            // The player sets up SCUM during the session...
            store.Save(new AppSettings(WorkspacePath: Path.Combine(root, "ws"), GamePath: game, AesKey: "KEY"));
            vm.ApplyGameConfiguration();

            // ...and the whole app comes alive, with no restart.
            Assert.True(vm.CanLaunch);                        // sidebar launch woke up
            Assert.True(vm.Build.CanLaunch);                  // Build's launch woke up
            Assert.True(content.Reads > readsBefore);         // the Workspace re-read the game
            Assert.Single(vm.Mods.Mods);                      // Mods found the real folder
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }
}
