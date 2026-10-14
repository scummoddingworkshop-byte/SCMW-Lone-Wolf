// tests/SCMW.Backend.Pak.Tests/ItemIconExtractorTests.cs
// Unit tests for the resumable, atomic, 128px icon extraction service. Everything
// runs against a tiny in-memory IItemIconSource and real SkiaSharp bitmaps, so no
// SCUM install is needed. Review Focus 1 (no-icon items) and 2 (resume) are pinned
// here; the env-gated real test lives in RealGameIntegrationTests.cs.
using System.Runtime.CompilerServices;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SkiaSharp;
using Xunit;

public sealed class ItemIconExtractorTests : IDisposable
{
    private const string Bow = "SCUM/Content/ConZ_Files/Items/Weapons/Ranged_Weapons/Improvised_Bow";
    private const string Spaghetti = "SCUM/Content/ConZ_Files/Items/Food/CannedSpaghetti";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "scmw-icons-" + Guid.NewGuid().ToString("N"));

    public ItemIconExtractorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } // best-effort cleanup
    }

    private string NewDir([CallerMemberName] string name = "")
    {
        var dir = Path.Combine(_root, name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ----- fresh extraction + progress ------------------------------------

    [Fact]
    public void Extract_writes_a_png_and_reports_progress()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var source = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => Bitmap(256, 256));
        var progress = new CaptureProgress();

        var result = new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow }, progress);

        Assert.Equal(new IconExtractionProgress(1, 1, 0, 0), result);
        var path = cache.GetIconPath(Bow);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path!).Length > 0);
        using var decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);
        Assert.True(decoded!.Width <= 128 && decoded.Height <= 128);

        Assert.NotEmpty(progress.Reports);
        Assert.Contains(progress.Reports, p => p is { Done: 1, Total: 1 });
    }

    [Fact]
    public void An_older_extractor_version_retries_items_recorded_as_no_icon()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);

        // A no-icon record written by an older extractor (a stale "# extract 1" header).
        File.WriteAllLines(IconCachePaths.ManifestPathFor(dir), new[] { "# extract 1", Bow });

        var source = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => Bitmap(64, 64));

        var result = new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.NotNull(cache.GetIconPath(Bow));   // retried and written, not skipped
        Assert.Equal(1, result.Done);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void A_recorded_no_icon_item_is_skipped_on_the_next_run()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var source = new FakeIconSource().NoIcon(Bow);

        new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow });   // records it
        var second = new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(0, second.Done);
        Assert.Equal(1, second.Skipped);
    }

    [Theory]
    [InlineData(512, 256, 128, 64)]
    [InlineData(256, 512, 64, 128)]
    [InlineData(300, 100, 128, 43)]
    [InlineData(64, 64, 64, 64)] // already small: never upscaled
    public void Extract_fits_inside_128_preserving_aspect_and_never_upscales(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var source = new FakeIconSource()
            .Resolves(Bow, "TEX")
            .Decodes("TEX", () => Bitmap(width, height));

        new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow });

        using var decoded = SKBitmap.Decode(cache.GetIconPath(Bow));
        Assert.NotNull(decoded);
        Assert.Equal(expectedWidth, decoded!.Width);
        Assert.Equal(expectedHeight, decoded.Height);
    }

    // ----- Review Focus 2: resume / skip ----------------------------------

    [Fact]
    public void Second_run_skips_existing_pngs_without_redecoding()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var first = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => Bitmap(256, 256));
        new ItemIconExtractor(first, cache, "build-1", 1).Extract(new[] { Bow });

        // A second source that would explode if the cached item were touched again.
        var second = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => throw new InvalidOperationException("re-decoded a cached icon"));

        var result = new ItemIconExtractor(second, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(0, 1, 1, 0), result);
        Assert.Equal(0, second.ResolveCount);
        Assert.Equal(0, second.DecodeCount);
    }

    // ----- Review Focus 1: no-icon manifest -------------------------------

    [Fact]
    public void No_icon_item_is_manifest_recorded_and_not_retried()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var manifestPath = IconCachePaths.ManifestPathFor(dir);

        var first = new FakeIconSource().NoIcon(Bow);
        var firstResult = new ItemIconExtractor(first, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(1, 1, 0, 0), firstResult);
        Assert.True(File.Exists(manifestPath));
        var lines = File.ReadAllLines(manifestPath);
        Assert.Equal(1, lines.Count(l => l.Trim() == Bow)); // recorded exactly once
        Assert.Null(cache.GetIconPath(Bow));

        var second = new FakeIconSource().NoIcon(Bow);
        var secondResult = new ItemIconExtractor(second, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(0, 1, 1, 0), secondResult);
        Assert.Equal(0, second.ResolveCount); // never re-resolved
    }

    [Fact]
    public void New_build_invalidates_the_no_icon_manifest_so_items_are_retried()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);

        new ItemIconExtractor(new FakeIconSource().NoIcon(Bow), cache, "build-1", 1)
            .Extract(new[] { Bow });

        // A newer build may ship icons: the no-icon entry must not block a retry.
        var source = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => Bitmap(128, 128));
        var result = new ItemIconExtractor(source, cache, "build-2", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(1, 1, 0, 0), result);
        Assert.NotNull(cache.GetIconPath(Bow));
    }

    // ----- retryable failures ---------------------------------------------

    [Fact]
    public void Unloadable_item_counts_failed_and_is_not_manifest_recorded()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var manifestPath = IconCachePaths.ManifestPathFor(dir);

        var first = new FakeIconSource().NotLoaded(Bow);
        var firstResult = new ItemIconExtractor(first, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(0, 1, 0, 1), firstResult);
        Assert.False(File.Exists(manifestPath)); // must stay retryable

        var second = new FakeIconSource().NotLoaded(Bow);
        var secondResult = new ItemIconExtractor(second, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(0, 1, 0, 1), secondResult);
        Assert.Equal(1, second.ResolveCount); // retried, not skipped
    }

    [Fact]
    public void Undecodable_texture_counts_failed_and_the_run_continues()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var source = new FakeIconSource()
            .Resolves(Bow, "TEX_BAD")
            .Decodes("TEX_BAD", () => null)
            .Resolves(Spaghetti, "TEX_GOOD")
            .Decodes("TEX_GOOD", () => Bitmap(200, 200));

        var result = new ItemIconExtractor(source, cache, "build-1", 1)
            .Extract(new[] { Bow, Spaghetti });

        Assert.Equal(new IconExtractionProgress(1, 2, 0, 1), result);
        Assert.Null(cache.GetIconPath(Bow));
        Assert.NotNull(cache.GetIconPath(Spaghetti));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    // ----- atomicity ------------------------------------------------------

    [Fact]
    public void Failed_write_leaves_no_partial_png_and_no_temp_files()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var finalPath = IconCachePaths.IconPathFor(dir, Bow);

        // A directory at the destination makes the atomic move fail: the run must
        // report Failed, leave the destination untouched, and clean up its temp.
        Directory.CreateDirectory(finalPath);

        var source = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => Bitmap(128, 128));

        var result = new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow });

        Assert.Equal(new IconExtractionProgress(0, 1, 0, 1), result);
        Assert.True(Directory.Exists(finalPath));
        Assert.False(File.Exists(finalPath));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void Stray_temp_files_from_an_earlier_crash_are_cleaned_up()
    {
        var dir = NewDir();
        var stray = Path.Combine(dir, "leftover.png.tmp");
        File.WriteAllText(stray, "half a png");

        var source = new FakeIconSource().NoIcon(Bow);
        new ItemIconExtractor(source, new FileIconCache(dir), "build-1", 1).Extract(new[] { Bow });

        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    // ----- cancellation ---------------------------------------------------

    [Fact]
    public void Cancellation_stops_promptly_and_leaves_no_temp_files()
    {
        var dir = NewDir();
        var ids = Enumerable.Range(0, 50).Select(i => $"SCUM/Content/ConZ_Files/Items/Item_{i}").ToArray();
        var cts = new CancellationTokenSource();

        var source = new FakeIconSource { OnResolve = _ => cts.Cancel() };
        foreach (var id in ids)
        {
            source.Resolves(id, "TEX_" + id)
                  .Decodes("TEX_" + id, () => Bitmap(64, 64));
        }

        var extractor = new ItemIconExtractor(source, new FileIconCache(dir), "build-1", 1);

        Assert.ThrowsAny<OperationCanceledException>(
            () => extractor.Extract(ids, null, cts.Token));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    // ----- IIconCache surface --------------------------------------------

    [Fact]
    public void Cache_reports_count_bytes_and_clear_removes_pngs_and_manifest()
    {
        var dir = NewDir();
        var cache = new FileIconCache(dir);
        var source = new FakeIconSource()
            .Resolves(Bow, "TEX_BOW")
            .Decodes("TEX_BOW", () => Bitmap(128, 128))
            .NoIcon(Spaghetti);

        new ItemIconExtractor(source, cache, "build-1", 1).Extract(new[] { Bow, Spaghetti });

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TotalBytes > 0);
        Assert.NotNull(cache.GetIconPath(Bow));
        Assert.True(File.Exists(IconCachePaths.ManifestPathFor(dir)));

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
        Assert.Null(cache.GetIconPath(Bow));
        Assert.False(File.Exists(IconCachePaths.ManifestPathFor(dir)));
    }

    // ----- helpers --------------------------------------------------------

    private static SKBitmap Bitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(200, 40, 40));
        return bitmap;
    }

    private sealed class CaptureProgress : IProgress<IconExtractionProgress>
    {
        private readonly List<IconExtractionProgress> _reports = new();

        public IReadOnlyList<IconExtractionProgress> Reports
        {
            get { lock (_reports) return _reports.ToArray(); }
        }

        public void Report(IconExtractionProgress value)
        {
            lock (_reports) _reports.Add(value);
        }
    }

    private sealed class FakeIconSource : IItemIconSource
    {
        private readonly Dictionary<string, IconSourceResult> _results = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<SKBitmap?>> _decoders = new(StringComparer.Ordinal);
        private int _resolves;
        private int _decodes;

        public int ResolveCount => Volatile.Read(ref _resolves);

        public int DecodeCount => Volatile.Read(ref _decodes);

        /// <summary>Invoked at the start of every Resolve (lets a test cancel mid-run).</summary>
        public Action<string>? OnResolve { get; set; }

        public FakeIconSource Resolves(string assetId, string texturePath)
        {
            _results[assetId] = IconSourceResult.Resolved(texturePath);
            return this;
        }

        public FakeIconSource NoIcon(string assetId)
        {
            _results[assetId] = IconSourceResult.NoIcon;
            return this;
        }

        public FakeIconSource NotLoaded(string assetId)
        {
            _results[assetId] = IconSourceResult.NotLoaded;
            return this;
        }

        public FakeIconSource Decodes(string texturePath, Func<SKBitmap?> factory)
        {
            _decoders[texturePath] = factory;
            return this;
        }

        public IconSourceResult Resolve(string assetId)
        {
            Interlocked.Increment(ref _resolves);
            OnResolve?.Invoke(assetId);
            return _results.TryGetValue(assetId, out var result) ? result : IconSourceResult.NotLoaded;
        }

        public SKBitmap? Decode(string texturePath)
        {
            Interlocked.Increment(ref _decodes);
            return _decoders.TryGetValue(texturePath, out var factory) ? factory() : null;
        }
    }
}
