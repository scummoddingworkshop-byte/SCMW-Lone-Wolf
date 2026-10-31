using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    /// The catalogue each source has already been read into, for as long as that source lives. A list
    /// switch should not re-read 8,000 rows nobody changed; a new game build (or a forced refresh) does.
    /// </summary>
    private static readonly ConditionalWeakTable<IContentSource, Catalogue> Catalogues = new();

    private sealed class Catalogue
    {
        public string? BuildId;
        public IReadOnlyList<AssetSummary>? Rows;
    }

    /// <summary>
    /// Every asset in the index, paged to exhaustion — never just the first page, or a capped page
    /// would silently hide files. Scans first: a fresh index is a fast no-op, while a missing or
    /// stale one is (re)built here — the one place a first run and a SCUM update are picked up, so
    /// the browse list can never be silently stale or empty.
    /// <para>
    /// The result is kept for the session: the next call (another list, the bulk table) answers from
    /// memory instead of reading the catalogue again. It is read again when the game's build changes.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<AssetSummary>> ReadAllAsync(
        IContentSource source, CancellationToken ct = default)
    {
        var status = await source.ScanAsync(null, ct).ConfigureAwait(false);

        var catalogue = Catalogues.GetValue(source, _ => new Catalogue());
        if (catalogue.Rows is not null && string.Equals(catalogue.BuildId, status.GameBuildId, System.StringComparison.Ordinal))
            return catalogue.Rows;

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

        catalogue.Rows = assets;
        catalogue.BuildId = status.GameBuildId;
        return assets;
    }

    /// <summary>
    /// Forget the catalogue kept for this source, so the next read comes from the game again. Used by
    /// **Read Game Data**, which exists precisely to re-read everything.
    /// </summary>
    public static void Forget(IContentSource source)
    {
        if (Catalogues.TryGetValue(source, out var catalogue))
        {
            catalogue.Rows = null;
            catalogue.BuildId = null;
        }
    }
}
