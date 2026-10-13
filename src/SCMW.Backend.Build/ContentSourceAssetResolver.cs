using SCMW.Core.Content;

namespace SCMW.Backend.Build;

/// <summary>
/// Turns a recorded item name (the screen's friendly name) into the reader's package path. Scans
/// first if needed, then requires exactly one exact-name match — an ambiguous name refuses.
/// </summary>
public sealed class ContentSourceAssetResolver : IAssetResolver
{
    private readonly IContentSource _source;

    public ContentSourceAssetResolver(IContentSource source) => _source = source;

    public async Task<AssetResolution> ResolveAsync(string recordedName, CancellationToken ct)
    {
        if (recordedName.Contains('/'))
            return new AssetResolution(recordedName, null);

        // Always ask the source to scan: it is idempotent (a fresh index is a fast no-op). This
        // also avoids the blocking "LastScan" check, which could freeze the window behind a scan.
        await _source.ScanAsync(null, ct).ConfigureAwait(false);

        var hits = await _source.SearchAsync(recordedName, null, 0, 500, ct).ConfigureAwait(false);
        var exact = hits
            .Where(h => string.Equals(h.Name, recordedName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exact.Count switch
        {
            1 => new AssetResolution(exact[0].Id, null),
            0 => new AssetResolution(null, $"\"{recordedName}\" was not found in the game's items."),
            _ => new AssetResolution(null, $"\"{recordedName}\" matches more than one item, so it cannot be built safely."),
        };
    }
}
