// src/SCMW.Core/Content/IAssetIndex.cs
// A local, queryable index of the pak scan results so the UI can search
// thousands of items instantly without re-mounting the pak. Pure storage
// contract: no CUE4Parse types, no game access.
namespace SCMW.Core.Content;

public interface IAssetIndex
{
    /// <summary>Creates the tables/indexes if they are missing. Idempotent.</summary>
    void EnsureSchema(string dbPath);

    /// <summary>
    /// Atomically replaces the whole index contents and stores the game
    /// build id the rows came from. On failure the previous contents remain.
    /// </summary>
    void ReplaceAll(string dbPath, string gameBuildId, IEnumerable<AssetSummary> rows);

    /// <summary>The build id stored by the last successful ReplaceAll, if any.</summary>
    string? ReadBuildId(string dbPath);

    /// <summary>The UTC time of the last successful ReplaceAll, or null if never scanned.</summary>
    DateTime? ReadLastScanUtc(string dbPath);

    /// <summary>Distinct categories of the indexed assets, ordered by name.</summary>
    IReadOnlyList<string> Categories(string dbPath);

    /// <summary>
    /// Case-insensitive substring search on the asset name, optionally
    /// filtered by category (case-insensitive), ordered by name, paged.
    /// </summary>
    IReadOnlyList<AssetSummary> Query(string dbPath, string query, string? category, int skip, int take);

    /// <summary>Number of indexed assets.</summary>
    int Count(string dbPath);

    /// <summary>True when the stored build id differs from the current one (or is missing).</summary>
    bool IsStale(string dbPath, string currentGameBuildId);
}
