// src/SCMW.Core/Content/IIconCache.cs
// Contract for the on-disk cache of extracted item icons. Core knows only about
// files and sizes; the pak-backed implementation (SCMW.Backend.Pak) does the
// reading, decoding and writing. No CUE4Parse or SkiaSharp types leak here, so
// the UI and Core can talk about icons without touching the game files.
namespace SCMW.Core.Content;

/// <summary>
/// A snapshot of an icon-extraction pass. <see cref="Total"/> is invariant for a
/// run; at the end <c>Done + Skipped + Failed == Total</c>.
/// </summary>
/// <param name="Done">Items processed this run (icon written, or confirmed to have no icon).</param>
/// <param name="Total">Total asset ids handed to the run.</param>
/// <param name="Skipped">Ids whose PNG was already cached, or already known to have no icon.</param>
/// <param name="Failed">Ids that could not be read/decoded this run (retryable; never manifest-recorded).</param>
public sealed record IconExtractionProgress(int Done, int Total, int Skipped, int Failed);

public interface IIconCache
{
    /// <summary>The folder PNGs (and the no-icon manifest) live in.</summary>
    string IconsDirectory { get; }

    /// <summary>The cached PNG path for an asset id, or null when it is not cached yet.</summary>
    string? GetIconPath(string assetId);

    /// <summary>Number of cached PNG files.</summary>
    int Count { get; }

    /// <summary>Total size in bytes of the cached PNG files.</summary>
    long TotalBytes { get; }

    /// <summary>Deletes every cached PNG and the no-icon manifest.</summary>
    void Clear();
}
