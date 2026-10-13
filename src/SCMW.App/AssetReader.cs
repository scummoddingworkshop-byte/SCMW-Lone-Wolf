using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SCMW.Core.Content;

namespace SCMW.App;

/// <summary>
/// Reads a kind's real files from the game for browsing — one home for the "page through the whole
/// index" loop that the Items screens and the Workspace all need, so the two can never drift.
/// </summary>
public static class AssetReader
{
    /// <summary>
    /// Every asset in the index, paged to exhaustion — never just the first page, or a capped page
    /// would silently hide files. Does not scan; the caller decides readiness.
    /// </summary>
    public static async Task<IReadOnlyList<AssetSummary>> ReadAllAsync(
        IContentSource source, CancellationToken ct = default)
    {
        var assets = new List<AssetSummary>();
        var skip = 0;
        while (true)
        {
            var page = await source.SearchAsync("", null, skip, ContentLimits.MaxTake, ct).ConfigureAwait(false);
            if (page.Count == 0) break;
            assets.AddRange(page);
            skip += page.Count;
            if (page.Count < ContentLimits.MaxTake) break;
            if (assets.Count > 200_000) break;   // safety: a source that ignores paging
        }

        return assets;
    }
}
