using Microsoft.Data.Sqlite;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Testing;
using SkiaSharp;
using Xunit;

// Env-gated tests against the owner's real SCUM install. Each uses [SkippableFact]
// so it reports SKIPPED (not passed/failed) unless both SCMW_SCUM_PATH and
// SCMW_TEST_AES_KEY are set. The AES key is read only from the environment and is
// never printed.
public class RealGameIntegrationTests
{
    private const string EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY";
    private const string SkipReason =
        "SCMW_SCUM_PATH and SCMW_TEST_AES_KEY must be set to run the real-install test.";

    private static (string PakPath, string Key) RequireEnv()
    {
        var pakPath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH");
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY");
        return (pakPath!, key!);
    }

    [SkippableFact(EnvVars = EnvVars, EnvSkipReason = SkipReason)]
    public async Task Real_install_mounts_and_lists_items()
    {
        var (pakPath, key) = RequireEnv();

        var src = new PakContentSource();
        src.Configure(pakPath, key, knownBuildId: null);

        var st = await src.ScanAsync();
        Assert.Equal(ContentSourceState.Ready, st.State);
        Assert.NotEmpty(await src.SearchAsync("", null, 0, 10));

        // A known item must appear (spike findings: Ranged_Weapons/Improvised_Bow).
        // Note: the game ships several similarly-named variants (Improvised_Bow_25,
        // _ES, CI_Improvised_Bow_35, ...), so match the exact package path.
        var all = await src.SearchAsync("Improvised Bow", null, 0, 500);
        var bow = Assert.Single(all, m =>
            m.TechnicalPath == "SCUM/Content/ConZ_Files/Items/Weapons/Ranged_Weapons/Improvised_Bow.uasset");
        Assert.Equal("Improvised Bow", bow.Name);
        Assert.Equal("Weapons", bow.Category);

        // Field loading must work for the known item (reads the CDO).
        var fields = await src.FieldsAsync(bow.Id);
        Assert.NotEmpty(fields);
    }

    [SkippableFact(EnvVars = EnvVars, EnvSkipReason = SkipReason)]
    public async Task Real_install_index_path_persists_to_sqlite_and_searches_it()
    {
        var (pakPath, key) = RequireEnv();

        // A temporary SQLite db exercises the production index path (as opposed to the
        // in-memory fallback used by the other gated test), which is otherwise only
        // covered by fake-mount unit tests.
        var dbPath = Path.Combine(Path.GetTempPath(), $"scmw-real-index-{Guid.NewGuid():N}.db");
        var index = new AssetIndex();
        try
        {
            var src = new PakContentSource(new PakMount(), index, dbPath);
            src.Configure(pakPath, key, knownBuildId: null);

            var st = await src.ScanAsync();
            Assert.Equal(ContentSourceState.Ready, st.State);

            // The scan persisted to SQLite, and the reported count matches IAssetIndex.Count.
            var count = index.Count(dbPath);
            Assert.True(count > 0);
            Assert.Equal(count, src.LastScan!.ItemCount);

            // Search is served from the index.
            Assert.NotEmpty(await src.SearchAsync("", null, 0, 10));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { } // best-effort cleanup
        }
    }

    [SkippableFact(EnvVars = EnvVars, EnvSkipReason = SkipReason)]
    public void Real_install_extracts_valid_128px_pngs_parallel_matches_sequential()
    {
        var (pakPath, key) = RequireEnv();

        // Known _ES-fallback item (Task 1: Improvised_Bow does not carry
        // GridInventoryIcon on its own CDO) plus a few other real, resolvable items.
        var itemRoot = "SCUM/Content/ConZ_Files/Items/";
        var ids = new List<string>
        {
            itemRoot + "Weapons/Ranged_Weapons/Improvised_Bow",
        };

        using var mount = new PakMount();
        Assert.True(mount.TryMount(pakPath, key, out var error), error);

        var source = new PakIconSource(mount);
        foreach (var path in mount.FilePaths)
        {
            if (ids.Count >= 5) break;
            if (!path.StartsWith(itemRoot, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = path[..^7];
            if (ids.Contains(id, StringComparer.Ordinal)) continue;
            if (source.Resolve(id).Outcome != IconSourceOutcome.Resolved) continue;

            ids.Add(id);
        }

        Assert.True(ids.Count >= 5, $"Only found {ids.Count} resolvable real items.");

        var seqDir = Path.Combine(Path.GetTempPath(), "scmw-icons-seq-" + Guid.NewGuid().ToString("N"));
        var parDir = Path.Combine(Path.GetTempPath(), "scmw-icons-par-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(seqDir);
        Directory.CreateDirectory(parDir);

        try
        {
            var seqCache = new FileIconCache(seqDir);
            var parCache = new FileIconCache(parDir);

            var seq = new ItemIconExtractor(source, seqCache, "real-test", 1).Extract(ids);
            var par = new ItemIconExtractor(source, parCache, "real-test", 8).Extract(ids);

            Assert.Equal(new IconExtractionProgress(ids.Count, ids.Count, 0, 0), seq);
            Assert.Equal(seq, par); // parallel produces the same results as sequential

            foreach (var id in ids)
            {
                var seqPath = seqCache.GetIconPath(id);
                var parPath = parCache.GetIconPath(id);
                Assert.NotNull(seqPath);
                Assert.NotNull(parPath);

                var bytes = File.ReadAllBytes(seqPath!);
                Assert.Equal(bytes, File.ReadAllBytes(parPath!)); // byte-for-byte identical

                // A sane PNG: real bytes, fitted inside 128px.
                Assert.True(bytes.Length > 100, $"{id}: implausibly small PNG ({bytes.Length} bytes).");
                Assert.Equal(0x89, bytes[0]);
                Assert.Equal((byte)'P', bytes[1]);
                Assert.Equal((byte)'N', bytes[2]);
                Assert.Equal((byte)'G', bytes[3]);

                using var bitmap = SKBitmap.Decode(seqPath);
                Assert.NotNull(bitmap);
                Assert.True(bitmap!.Width <= ItemIconExtractor.MaxIconDimension);
                Assert.True(bitmap.Height <= ItemIconExtractor.MaxIconDimension);
            }
        }
        finally
        {
            TryDeleteDirectory(seqDir);
            TryDeleteDirectory(parDir);
        }
    }

    [SkippableFact(EnvVars = EnvVars, EnvSkipReason = SkipReason)]
    public async Task Real_install_extracts_icons_through_the_content_source_into_a_cache()
    {
        var (pakPath, key) = RequireEnv();

        // Pick 5 real, resolvable items through the content source's own extraction path.
        // Improvised_Bow is first because Task 1 found its icon only on the _ES sibling.
        var itemRoot = "SCUM/Content/ConZ_Files/Items/";
        var ids = new List<string>
        {
            itemRoot + "Weapons/Ranged_Weapons/Improvised_Bow",
        };

        using (var mount = new PakMount())
        {
            Assert.True(mount.TryMount(pakPath, key, out var error), error);
            var iconSource = new PakIconSource(mount);
            foreach (var path in mount.FilePaths)
            {
                if (ids.Count >= 5) break;
                if (!path.StartsWith(itemRoot, StringComparison.OrdinalIgnoreCase) ||
                    !path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = path[..^7];
                if (ids.Contains(id, StringComparer.Ordinal)) continue;
                if (iconSource.Resolve(id).Outcome != IconSourceOutcome.Resolved) continue;

                ids.Add(id);
            }
        }

        Assert.True(ids.Count >= 5, $"Only found {ids.Count} resolvable real items.");

        var iconsDir = Path.Combine(Path.GetTempPath(), "scmw-icons-src-" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(Path.GetTempPath(), "scmw-icons-src-" + Guid.NewGuid().ToString("N") + ".db");
        var cache = new FileIconCache(iconsDir);
        try
        {
            var src = new PakContentSource(new PakMount(), new AssetIndex(), dbPath, cache);
            src.Configure(pakPath, key, knownBuildId: null);

            var status = await src.ScanAsync();
            Assert.Equal(ContentSourceState.Ready, status.State);

            // Subset overload: the same path the Assets screen uses, for a handful of ids.
            var result = await src.ExtractIconsAsync(ids);

            Assert.Equal(0, result.Failed);
            Assert.Equal(ids.Count, result.Done);

            foreach (var id in ids)
            {
                var iconPath = cache.GetIconPath(id);
                Assert.NotNull(iconPath);

                using var bitmap = SKBitmap.Decode(iconPath);
                Assert.NotNull(bitmap);
                Assert.True(bitmap!.Width <= ItemIconExtractor.MaxIconDimension);
                Assert.True(bitmap.Height <= ItemIconExtractor.MaxIconDimension);
            }

            Assert.Equal(ids.Count, cache.Count);
            Assert.True(cache.TotalBytes > 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
            TryDeleteDirectory(iconsDir);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
