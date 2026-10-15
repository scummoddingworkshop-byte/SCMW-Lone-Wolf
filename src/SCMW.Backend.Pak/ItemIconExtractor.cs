// src/SCMW.Backend.Pak/ItemIconExtractor.cs
// The resumable, atomic item-icon extraction service. For every asset id it:
//   1. skips it if a cached PNG already exists (or it is on the no-icon manifest);
//   2. resolves the item's icon texture (IItemIconSource.Resolve) — "could not
//      load" counts as a retryable failure and is never written off;
//   3. decodes the texture and fits it inside 128x128 (aspect preserved, never
//      upscaled);
//   4. writes <icons>/<assetId>.png atomically: encode to a ".png.tmp" file, then
//      move it into place. A failed item leaves neither a partial PNG nor a temp.
//
// A single item's failure is counted in Failed and never aborts the run. The
// no-icon manifest records only items that loaded fine but have no icon, so they
// are not retried; a game-build change clears it so a newer build can be retried.
//
// Parallelism: bounded (<= 8) via Parallel.ForEach. Task 1 observed read/load/decode
// safe at degree 8, provider caches are concurrent dictionaries, and the gated real
// test re-verifies parallel vs sequential byte-for-byte. Counters and progress are
// guarded, and the manifest has its own gate.
using SCMW.Core.Content;
using SkiaSharp;

namespace SCMW.Backend.Pak;

public sealed class ItemIconExtractor
{
    /// <summary>Icons are fitted inside 128x128; smaller icons are left at their own size.</summary>
    public const int MaxIconDimension = 128;

    /// <summary>Hard upper bound on extraction parallelism (Task 1 verdict).</summary>
    public const int MaxDegreeOfParallelism = 8;

    private readonly IItemIconSource _source;
    private readonly IIconCache _cache;
    private readonly string? _buildId;
    private readonly int _parallelism;

    public ItemIconExtractor(
        IItemIconSource source,
        IIconCache cache,
        string? buildId = null,
        int maxDegreeOfParallelism = MaxDegreeOfParallelism)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _buildId = buildId;
        _parallelism = Math.Clamp(maxDegreeOfParallelism, 1, MaxDegreeOfParallelism);
    }

    /// <summary>
    /// Extracts icons for <paramref name="assetIds"/>. Ids are de-duplicated and
    /// order-insensitive. Throws <see cref="OperationCanceledException"/> when
    /// <paramref name="ct"/> fires; any temp files from in-flight items are removed.
    /// </summary>
    public IconExtractionProgress Extract(
        IEnumerable<string> assetIds,
        IProgress<IconExtractionProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        Directory.CreateDirectory(_cache.IconsDirectory);
        DeleteStrayTemps();

        var manifest = IconNoIconManifest.Load(_cache.IconsDirectory, _buildId);
        var ids = assetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var total = ids.Length;

        var done = 0;
        var skipped = 0;
        var failed = 0;

        void Snapshot()
            => progress?.Report(new IconExtractionProgress(
                Volatile.Read(ref done), total, Volatile.Read(ref skipped), Volatile.Read(ref failed)));

        ct.ThrowIfCancellationRequested();
        Snapshot();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _parallelism,
            CancellationToken = ct,
        };

        try
        {
            Parallel.ForEach(ids, options, id =>
            {
                ct.ThrowIfCancellationRequested();

                var finalPath = IconCachePaths.IconPathFor(_cache.IconsDirectory, id);

                // Resume/skip: an existing PNG, or a previously confirmed no-icon item.
                if (File.Exists(finalPath) || manifest.Contains(id))
                {
                    Interlocked.Increment(ref skipped);
                    Snapshot();
                    return;
                }

                try
                {
                    var result = _source.Resolve(id);
                    switch (result.Outcome)
                    {
                        case IconSourceOutcome.NotLoaded:
                            // Retryable: could not load the package / not an item.
                            Interlocked.Increment(ref failed);
                            break;

                        case IconSourceOutcome.NoIcon:
                            // Loaded fine, definitively no icon: record permanently.
                            manifest.Add(id);
                            Interlocked.Increment(ref done);
                            break;

                        case IconSourceOutcome.Resolved:
                            if (TryWriteIcon(id, result.TexturePath!, finalPath))
                                Interlocked.Increment(ref done);
                            else
                                Interlocked.Increment(ref failed);
                            break;

                        default:
                            Interlocked.Increment(ref failed);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // One bad item never aborts the run.
                    Interlocked.Increment(ref failed);
                }

                Snapshot();
            });
        }
        finally
        {
            DeleteStrayTemps();
        }

        return new IconExtractionProgress(done, total, skipped, failed);
    }

    /// <summary>
    /// Decodes, downscales and atomically writes one icon. Returns false on any
    /// failure, always leaving the destination untouched and no temp behind.
    /// </summary>
    private bool TryWriteIcon(string assetId, string texturePath, string finalPath)
    {
        var tempPath = finalPath + IconCachePaths.TempExtension;
        SKBitmap? decoded = null;
        SKBitmap? scaled = null;

        try
        {
            decoded = _source.Decode(texturePath);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
                return false;

            scaled = Downscale(decoded);
            if (scaled is null)
                return false;

            using var image = SKImage.FromBitmap(scaled);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null || data.Size == 0)
                return false;

            File.WriteAllBytes(tempPath, data.ToArray());
            File.Move(tempPath, finalPath, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            TryDelete(tempPath);
            if (!ReferenceEquals(scaled, decoded))
                scaled?.Dispose();
            decoded?.Dispose();
        }
    }

    /// <summary>
    /// Fits a bitmap inside <see cref="MaxIconDimension"/>x<see cref="MaxIconDimension"/>
    /// preserving aspect. Returns the source unchanged when it already fits (never
    /// upscales), or null when the resize fails.
    /// </summary>
    private static SKBitmap? Downscale(SKBitmap source)
    {
        if (source.Width <= MaxIconDimension && source.Height <= MaxIconDimension)
            return source;

        var scale = Math.Min(
            (double)MaxIconDimension / source.Width,
            (double)MaxIconDimension / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        return source.Resize(info, SKFilterQuality.High);
    }

    private void DeleteStrayTemps()
    {
        if (!Directory.Exists(_cache.IconsDirectory))
            return;

        foreach (var temp in Directory.EnumerateFiles(
                     _cache.IconsDirectory, "*" + IconCachePaths.TempExtension))
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The <c>&lt;icons&gt;\_no-icon.txt</c> manifest: one asset id per line, plus an
/// optional <c># build &lt;id&gt;</c> header line. Ids are only ever added (once an
/// item is confirmed icon-less for a build); a newer build clears the list so
/// extraction retries, because a game update may add icons.
/// </summary>
internal sealed class IconNoIconManifest
{
    private const string BuildHeaderPrefix = "# build ";
    private const string ExtractHeaderPrefix = "# extract ";
    private const string FileHeader = "# Items that loaded fine but have no icon; do not retry.";

    /// <summary>
    /// The icon-resolution logic's version. Bump it whenever <see cref="PakIconSource"/> learns to find
    /// an icon it could not before (e.g. recipes, base elements), so the no-icon list is discarded and
    /// every asset is retried. Without this, a fix could never take effect for already-recorded items.
    /// </summary>
    public const string ExtractVersion = "3";

    private readonly string _path;
    private readonly HashSet<string> _ids;
    private readonly string? _buildId;
    private readonly object _gate = new();

    private IconNoIconManifest(string path, HashSet<string> ids, string? buildId)
    {
        _path = path;
        _ids = ids;
        _buildId = buildId;
    }

    public static IconNoIconManifest Load(string iconsDirectory, string? buildId)
    {
        var path = IconCachePaths.ManifestPathFor(iconsDirectory);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? storedBuild = null;
        string? storedExtract = null;
        var fileExists = File.Exists(path);

        if (fileExists)
        {
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith(ExtractHeaderPrefix, StringComparison.Ordinal))
                {
                    storedExtract = line[ExtractHeaderPrefix.Length..].Trim();
                    continue;
                }
                if (line.StartsWith(BuildHeaderPrefix, StringComparison.Ordinal))
                {
                    storedBuild = line[BuildHeaderPrefix.Length..].Trim();
                    continue;
                }
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                ids.Add(line);
            }
        }

        var manifest = new IconNoIconManifest(path, ids, buildId);

        // A newer extractor invalidates the no-icon list outright: it may now find an icon where it
        // could not before (recipes and base elements were the case), so everything is retried.
        // A new game build also invalidates it: the game may now ship icons.
        // (An old file with no build header keeps its ids and just gains a header.)
        var extractChanged = fileExists && !string.Equals(storedExtract, ExtractVersion, StringComparison.Ordinal);
        var buildChanged = fileExists && !string.IsNullOrWhiteSpace(buildId)
                           && !string.Equals(storedBuild, buildId, StringComparison.Ordinal);

        if (extractChanged)
        {
            ids.Clear();
            manifest.Rewrite(buildId);
        }
        else if (buildChanged)
        {
            if (!string.IsNullOrWhiteSpace(storedBuild))
                ids.Clear();
            manifest.Rewrite(buildId);
        }

        return manifest;
    }

    public bool Contains(string assetId)
    {
        lock (_gate)
            return _ids.Contains(assetId);
    }

    /// <summary>Adds an id (durably, so an interrupted run keeps the record) if new.</summary>
    public void Add(string assetId)
    {
        lock (_gate)
        {
            if (!_ids.Add(assetId))
                return;

            if (!File.Exists(_path))
            {
                var header = $"{FileHeader}{Environment.NewLine}"
                           + $"{ExtractHeaderPrefix}{ExtractVersion}{Environment.NewLine}";
                if (!string.IsNullOrWhiteSpace(_buildId))
                    header += $"{BuildHeaderPrefix}{_buildId}{Environment.NewLine}";
                File.WriteAllText(_path, header);
            }

            File.AppendAllText(_path, assetId + Environment.NewLine);
        }
    }

    /// <summary>Rewrites the file from the current ids and the header lines.</summary>
    private void Rewrite(string? buildId)
    {
        lock (_gate)
        {
            var text = $"{FileHeader}{Environment.NewLine}"
                     + $"{ExtractHeaderPrefix}{ExtractVersion}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(buildId))
                text += $"{BuildHeaderPrefix}{buildId}{Environment.NewLine}";
            foreach (var id in _ids)
                text += id + Environment.NewLine;
            File.WriteAllText(_path, text);
        }
    }
}
