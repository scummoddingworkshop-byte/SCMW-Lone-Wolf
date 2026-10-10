using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

public class BuildIconTests
{
    // A real (tiny) 1x1 PNG, so the bitmap really loads.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [AvaloniaFact]
    public async Task A_build_row_gets_the_items_picture()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var png = Path.Combine(root, "icon.png");
            File.WriteAllBytes(png, Convert.FromBase64String(TinyPngBase64));

            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);

            var source = new IconSource("Duct Tape", "SCUM/.../Duct_Tape_ES", png);
            var vm = new BuildViewModel(changes, projects: projects, source: source);

            await vm.RequestIconsAsync();

            Assert.NotNull(vm.Changes[0].Picture);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_row_whose_item_cannot_be_found_keeps_the_fallback_mark()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Ghost Item", "Max stack", "10", "25", projects.Current!.Id);

            var source = new IconSource("Duct Tape", "SCUM/.../Duct_Tape_ES", @"C:\no\icon.png");
            var vm = new BuildViewModel(changes, projects: projects, source: source);

            await vm.RequestIconsAsync();

            Assert.Null(vm.Changes[0].Picture);
        }
        finally { TestProjects.Cleanup(root); }
    }

    /// <summary>A source with one findable item and a cached icon path; no game, no disk reads.</summary>
    private sealed class IconSource : IContentSource
    {
        private readonly string _name;
        private readonly string _id;

        public IconSource(string name, string id, string iconPath)
        {
            _name = name;
            _id = id;
            Icons = new IconCache(id, iconPath);
        }

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons { get; }
        public IReadOnlyList<string> Categories => new[] { "Weapons" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(
                string.Equals(q, _name, StringComparison.OrdinalIgnoreCase)
                    ? new[] { new AssetSummary(_id, _name, "Weapons", StatusKind.Unknown, 0, _id + ".uasset", null) }
                    : Array.Empty<AssetSummary>());

        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());

        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));

        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));

        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class IconCache : IIconCache
    {
        private readonly string _id;
        private readonly string? _path;

        public IconCache(string id, string? path)
        {
            _id = id;
            _path = path;
        }

        public string IconsDirectory => "";
        public string? GetIconPath(string assetId) => assetId == _id ? _path : null;
        public int Count => 0;
        public long TotalBytes => 0;
        public void Clear() { }
    }
}
