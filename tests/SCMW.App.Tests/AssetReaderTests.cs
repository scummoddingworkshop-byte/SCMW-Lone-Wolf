using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

/// <summary>The shared "page through a kind's real files" reader: it must scan before it reads, so
/// a first run builds the index and a SCUM update re-reads it — the one place that happens now.</summary>
public class AssetReaderTests
{
    [Fact]
    public async Task ReadAllAsync_builds_the_index_by_scanning_first()
    {
        var source = new ScanFirstSource();

        var rows = await AssetReader.ReadAllAsync(source);

        Assert.True(source.Scanned,
            "the reader must scan before it reads, so a first run or a game update is picked up");
        var row = Assert.Single(rows);
        Assert.Equal("Tomato", row.Name);
    }

    /// <summary>A source that only returns rows once it has been scanned — so a reader that skips
    /// the scan sees nothing (the old bug: a stale or silently-empty list).</summary>
    private sealed class ScanFirstSource : IContentSource
    {
        public bool Scanned;
        private bool _ready;

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => Array.Empty<string>();
        public void Configure(string p, string? k, string? b) { }

        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default)
        {
            Scanned = true;
            _ready = true;
            return Task.FromResult(Status);
        }

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetSummary>>(!_ready || s > 0
                ? Array.Empty<AssetSummary>()
                : new[] { new AssetSummary("id", "Tomato", "Food", StatusKind.Editable, 1, "id.uasset", null) });

        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
