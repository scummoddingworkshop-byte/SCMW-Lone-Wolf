// src/SCMW.Core/Content/IContentSource.cs
// The contract both the real pak-backed source (SCMW.Backend.Pak) and the
// demo/fallback source implement, so Core and the UI never talk to CUE4Parse
// types directly.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SCMW.Core.Content;

public interface IContentSource
{
    ContentSourceStatus Status { get; }

    /// <summary>The last successful scan recorded in the index, or null when never scanned.</summary>
    ScanInfo? LastScan { get; }

    /// <summary>
    /// The on-disk cache of extracted item icons. UI code resolves a row's image through
    /// <see cref="IIconCache.GetIconPath"/> only — never by building the path itself.
    /// </summary>
    IIconCache Icons { get; }

    void Configure(string pakPath, string? aesKey, string? knownBuildId);
    Task<ContentSourceStatus> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<AssetSummary>> SearchAsync(string query, string? category,
        int skip = 0, int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<AssetField>> FieldsAsync(string assetId, CancellationToken ct = default);
    IReadOnlyList<string> Categories { get; }

    /// <summary>
    /// Extracts icons for every indexed item, in the background. The pass is resumable
    /// (already-cached items are skipped) and never blocks searching (search is served
    /// from the index). A cancelled/failed pass never corrupts the cache.
    /// </summary>
    Task<IconExtractionProgress> ExtractAllIconsAsync(
        IProgress<IconExtractionProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Extracts icons for a specific subset of asset ids (progressive use and tests).
    /// Same guarantees as <see cref="ExtractAllIconsAsync"/>.
    /// </summary>
    Task<IconExtractionProgress> ExtractIconsAsync(
        IEnumerable<string> assetIds,
        IProgress<IconExtractionProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes every cached icon and the no-icon manifest. Serialised against
    /// extraction: a running pass is stopped first and no pass may write while the
    /// clear is in progress, so the cache can never be repopulated mid-clear.
    /// Afterwards the app falls back to letter tiles until a later pass re-extracts.
    /// </summary>
    Task ClearIconsAsync(CancellationToken ct = default);
}
