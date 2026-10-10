using SCMW.Backend.Build;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.Backend.Build.Tests;

public class ContentSourceAssetResolverTests
{
    private sealed class FakeSource : IContentSource
    {
        public readonly List<AssetSummary> Items = new();
        public int Scans;

        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default)
        { Scans++; return Task.FromResult(new ContentSourceStatus(ContentSourceState.Ready, "", null)); }

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int skip = 0, int take = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetSummary>>(
                Items.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList());

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "", null);
        public ScanInfo? LastScan => Scans > 0 ? new ScanInfo(DateTime.UtcNow, Items.Count, null) : null;
        public IIconCache Icons => throw new NotImplementedException();
        public void Configure(string p, string? k, string? b) { }
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public IReadOnlyList<string> Categories => Array.Empty<string>();
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ClearIconsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static AssetSummary Item(string id, string name) =>
        new(id, name, "Equipment", StatusKind.Unknown, 0, id + ".uasset", null);

    [Fact]
    public async Task A_unique_name_resolves_to_its_package_path()
    {
        var source = new FakeSource();
        source.Items.Add(Item("SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES", "Duct Tape ES"));

        var result = await new ContentSourceAssetResolver(source).ResolveAsync("Duct Tape ES", CancellationToken.None);

        Assert.Equal("SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES", result.PackagePath);
    }

    [Fact]
    public async Task An_unscanned_source_is_scanned_first()
    {
        var source = new FakeSource();
        source.Items.Add(Item("SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES", "Duct Tape ES"));

        await new ContentSourceAssetResolver(source).ResolveAsync("Duct Tape ES", CancellationToken.None);

        Assert.Equal(1, source.Scans);
    }

    [Fact]
    public async Task A_duplicate_name_refuses_rather_than_guessing()
    {
        var source = new FakeSource();
        source.Items.Add(Item("SCUM/Content/A/Duct_Tape_ES", "Duct Tape ES"));
        source.Items.Add(Item("SCUM/Content/B/Duct_Tape_ES", "Duct Tape ES"));

        var result = await new ContentSourceAssetResolver(source).ResolveAsync("Duct Tape ES", CancellationToken.None);

        Assert.Null(result.PackagePath);
        Assert.Contains("more than one", result.Error);
    }

    [Fact]
    public async Task An_unknown_name_fails_plainly()
    {
        var source = new FakeSource();
        var result = await new ContentSourceAssetResolver(source).ResolveAsync("Ghost", CancellationToken.None);
        Assert.Null(result.PackagePath);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task A_package_path_is_used_as_is()
    {
        var source = new FakeSource();
        var result = await new ContentSourceAssetResolver(source)
            .ResolveAsync("SCUM/Content/ConZ_Files/Items/X_ES", CancellationToken.None);
        Assert.Equal("SCUM/Content/ConZ_Files/Items/X_ES", result.PackagePath);
    }
}
