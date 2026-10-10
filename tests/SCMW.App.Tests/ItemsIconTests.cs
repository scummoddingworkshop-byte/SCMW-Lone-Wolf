using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

public class ItemsIconTests
{
    private sealed class FakeIcons : IIconCache
    {
        private readonly Dictionary<string, string> _paths;
        public FakeIcons(Dictionary<string, string> paths) => _paths = paths;
        public string IconsDirectory => "";
        public string? GetIconPath(string assetId) => _paths.TryGetValue(assetId, out var p) ? p : null;
        public int Count => _paths.Count;
        public long TotalBytes => 0;
        public void Clear() { }
    }

    [AvaloniaFact]
    public void An_item_with_a_cached_icon_gets_a_path()
    {
        var id = "SCUM/.../Improvised_Bow_ES";
        var catalog = new ContentCatalog(null!, new FakeIcons(new() { [id] = @"C:\icons\bow.png" }));

        Assert.Equal(@"C:\icons\bow.png", catalog.IconPath(id));
        Assert.Null(catalog.IconPath("SCUM/.../No_Icon_Item"));
    }

    [AvaloniaFact]
    public async Task Requesting_icons_sets_the_path_on_items_that_have_one()
    {
        var source = FakeSource();
        var icons = new FakeIcons(new() { ["SCUM/.../Improvised_Bow_ES"] = @"C:\icons\bow.png" });
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source, icons));
        await vm.LoadFromSourceAsync();

        await vm.RequestIconsAsync();

        var bow = vm.VisibleItems.First(i => i.Name == "Improvised Bow");
        Assert.Equal(@"C:\icons\bow.png", bow.IconPath);
    }

    private static IContentSource FakeSource() => new StubSource();

    /// <summary A source with one item and no fields; no game, no disk.</summary>
    private sealed class StubSource : IContentSource
    {
        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Weapons" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(new[]
            {
                new AssetSummary("SCUM/.../Improvised_Bow_ES", "Improvised Bow", "Weapons",
                    StatusKind.Unknown, 0, "SCUM/.../Improvised_Bow_ES.uasset", null),
            });
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}

