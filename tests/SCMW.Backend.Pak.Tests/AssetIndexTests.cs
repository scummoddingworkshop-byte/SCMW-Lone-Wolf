using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SCMW.Backend.Pak;
using SCMW.Core;
using SCMW.Core.Content;

// Local SQLite asset index: schema lifecycle, transactional replace,
// paging/query semantics, build-id staleness, and a large-index sanity run.
// All tests use temp db files only — no game access, no network, no paks.
public class AssetIndexTests
{
    [Fact]
    public void EnsureSchema_is_idempotent()
    {
        using var db = new TempDb();
        var index = new AssetIndex();

        index.EnsureSchema(db.Path);
        index.EnsureSchema(db.Path); // second call must be a no-op, not an error

        Assert.Equal(0, index.Count(db.Path));
    }

    [Fact]
    public void ReplaceAll_then_Count_and_Query_roundtrip()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        index.EnsureSchema(db.Path);
        index.ReplaceAll(db.Path, "125621", new[]
        {
            new AssetSummary("Weapons/Ranged_Weapons/Improvised_Bow", "Improvised Bow", "Weapons",
                StatusKind.Editable, 3,
                "SCUM/Content/ConZ_Files/Items/Weapons/Ranged_Weapons/Improvised_Bow.uasset", null),
            new AssetSummary("Food/Canned_Food/CannedSpaghetti", "CannedSpaghetti", "Food",
                StatusKind.ReadOnly, 0,
                "SCUM/Content/ConZ_Files/Items/Food/Canned_Food/CannedSpaghetti.uasset", "ico_canned"),
            new AssetSummary("Medical/First_Aid_Kit", "First Aid Kit", "Medical",
                StatusKind.Unknown, 0,
                "SCUM/Content/ConZ_Files/Items/Medical/First_Aid_Kit.uasset", null),
        });

        Assert.Equal(3, index.Count(db.Path));

        // Name substring match is case-insensitive.
        var bow = Assert.Single(index.Query(db.Path, "bow", null, 0, 100));
        Assert.Single(index.Query(db.Path, "BOW", null, 0, 100));

        // The whole row round-trips through storage.
        Assert.Equal("Weapons/Ranged_Weapons/Improvised_Bow", bow.Id);
        Assert.Equal("Weapons", bow.Category);
        Assert.Equal(StatusKind.Editable, bow.Overall);
        Assert.Equal(3, bow.EditableCount);
        Assert.Equal("SCUM/Content/ConZ_Files/Items/Weapons/Ranged_Weapons/Improvised_Bow.uasset",
            bow.TechnicalPath);
        Assert.Null(bow.IconKey);

        // Nullable icon_key round-trips a non-null value too.
        var spaghetti = Assert.Single(index.Query(db.Path, "spaghetti", null, 0, 100));
        Assert.Equal("ico_canned", spaghetti.IconKey);
        Assert.Equal(StatusKind.ReadOnly, spaghetti.Overall);

        // Category filter is case-insensitive; unknown categories match nothing.
        var food = Assert.Single(index.Query(db.Path, "", "food", 0, 100));
        Assert.Equal("CannedSpaghetti", food.Name);
        Assert.Empty(index.Query(db.Path, "", "Drink", 0, 100));
        Assert.Empty(index.Query(db.Path, "zzz-no-such-item", null, 0, 100));
    }

    [Fact]
    public void Query_honors_skip_and_take_cap()
    {
        // Cap rule under test: take is clamped to [0, 500]. With only 20 rows
        // stored, take = 9999 returns all 20 — the cap limits page size, it
        // never invents or truncates the data itself.
        using var db = new TempDb();
        var index = new AssetIndex();
        index.ReplaceAll(db.Path, "125621", Rows("Equipment", 20));

        Assert.Equal(5, index.Query(db.Path, "", null, skip: 0, take: 5).Count);
        Assert.Equal(20, index.Query(db.Path, "", null, skip: 0, take: 9999).Count);

        var page = index.Query(db.Path, "", null, skip: 15, take: 10);
        Assert.Equal(5, page.Count);
        Assert.Equal("Item 0015", page[0].Name); // ordered by name
        Assert.Equal("Item 0019", page[^1].Name);

        Assert.Empty(index.Query(db.Path, "", null, skip: 20, take: 5));  // past the end
        Assert.Empty(index.Query(db.Path, "", null, skip: 0, take: 0));   // take floor
        Assert.Equal(5, index.Query(db.Path, "", null, skip: -7, take: 5).Count); // skip clamps to 0
    }

    [Fact]
    public void Paging_over_duplicate_names_returns_every_row_exactly_once()
    {
        // Names collide, so without the `id` tiebreaker OFFSET paging is not
        // guaranteed stable. Insert in an order that differs from id order, then
        // page through: every row must appear exactly once, in (name, id) order.
        using var db = new TempDb();
        var index = new AssetIndex();
        var rows = Enumerable.Range(0, 30)
            .Reverse()
            .Select(i => new AssetSummary(
                $"Equipment/Dup_{i:D2}", "Duplicate", "Equipment", StatusKind.Unknown, 0,
                $"SCUM/Content/ConZ_Files/Items/Equipment/Dup_{i:D2}.uasset", null))
            .ToArray();
        index.ReplaceAll(db.Path, "125621", rows);

        const int pageSize = 7;
        var seen = new List<string>();
        for (var skip = 0; skip < rows.Length; skip += pageSize)
        {
            seen.AddRange(index.Query(db.Path, "", null, skip, pageSize).Select(r => r.Id));
        }

        var expected = rows.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(30, seen.Count);
        Assert.Equal(30, seen.Distinct().Count());
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void ReplaceAll_is_transactional()
    {
        // Failure is forced with a duplicate primary key mid-sequence: the
        // third row repeats the second row's id, so SQLite rejects the insert
        // after earlier rows of the same batch were already written. Because
        // ReplaceAll commits once at the end, the violation must roll back the
        // whole batch: the previous contents survive, and the rows inserted
        // before the violation must not linger.
        using var db = new TempDb();
        var index = new AssetIndex();
        index.ReplaceAll(db.Path, "125621", Rows("Weapons", 3));
        Assert.Equal(3, index.Count(db.Path));

        var failing = new[]
        {
            Row("New_Gadget", "New Gadget", "Equipment"), // written before the violation
            Row("Dup", "Dup A", "Equipment"),
            Row("Dup", "Dup A", "Equipment"),            // duplicate id -> PK violation
        };

        Assert.Throws<SqliteException>(() => index.ReplaceAll(db.Path, "999", failing));

        Assert.Equal(3, index.Count(db.Path));                          // previous contents remain
        Assert.Equal("125621", index.ReadBuildId(db.Path));             // meta write rolled back too
        Assert.Empty(index.Query(db.Path, "New Gadget", null, 0, 100)); // partial batch rolled back
        Assert.Equal("Item 0000", index.Query(db.Path, "", "Weapons", 0, 100)[0].Name);
    }

    [Fact]
    public void ReadBuildId_roundtrip_and_IsStale()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        index.EnsureSchema(db.Path);
        index.ReplaceAll(db.Path, "125621", Rows("Weapons", 1));

        Assert.Equal("125621", index.ReadBuildId(db.Path));
        Assert.False(index.IsStale(db.Path, "125621"));
        Assert.True(index.IsStale(db.Path, "999")); // different build -> stale

        // Empty db (schema only, never replaced): missing build id is stale.
        using var empty = new TempDb();
        index.EnsureSchema(empty.Path);
        Assert.Null(index.ReadBuildId(empty.Path));
        Assert.True(index.IsStale(empty.Path, "anything"));

        // Never-created db: reads return defaults without creating the file.
        using var missing = new TempDb();
        Assert.Equal(0, index.Count(missing.Path));
        Assert.Empty(index.Query(missing.Path, "", null, 0, 100));
        Assert.True(index.IsStale(missing.Path, "anything"));
        Assert.False(File.Exists(missing.Path));
    }

    [Fact]
    public void Categories_and_last_scan_time_roundtrip()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        index.ReplaceAll(db.Path, "125621", new[]
        {
            Row("Improvised_Bow", "Improvised Bow", "Weapons"),
            Row("CannedSpaghetti", "Canned Spaghetti", "Food"),
            Row("Bandage", "Bandage", "Medical"),
            Row("Bow_2", "Bow 2", "Weapons"),
        });

        // Distinct categories, ordered by name.
        Assert.Equal(new[] { "Food", "Medical", "Weapons" }, index.Categories(db.Path));

        // ReplaceAll recorded the scan time (recent, UTC).
        var scanUtc = index.ReadLastScanUtc(db.Path);
        Assert.NotNull(scanUtc);
        var age = DateTime.UtcNow - scanUtc!.Value;
        Assert.InRange(age.TotalSeconds, -5, 120);

        // Never-scanned db has no categories and no scan time.
        using var empty = new TempDb();
        index.EnsureSchema(empty.Path);
        Assert.Empty(index.Categories(empty.Path));
        Assert.Null(index.ReadLastScanUtc(empty.Path));
    }

    [Fact]
    public void Large_index_query_is_fast_and_capped()
    {
        // One ReplaceAll = one transaction, so 50,000 rows land as a single
        // commit. The Stopwatch bound (2s) is a generous sanity check meant to
        // catch accidental O(n^2) behavior (e.g. per-row commits), not to
        // benchmark SQLite.
        using var db = new TempDb();
        var index = new AssetIndex();
        index.ReplaceAll(db.Path, "125621", Rows("Equipment", 50_000, width: 5));
        Assert.Equal(50_000, index.Count(db.Path));

        var sw = Stopwatch.StartNew();
        var page = index.Query(db.Path, "", null, skip: 0, take: 100);
        sw.Stop();

        Assert.Equal(100, page.Count);
        Assert.True(sw.Elapsed.TotalSeconds < 2,
            $"Query over 50k rows took {sw.Elapsed.TotalSeconds:0.00}s (expected well under 2s).");
    }

    private static IEnumerable<AssetSummary> Rows(string category, int count, int width = 4)
        => Enumerable.Range(0, count).Select(i => new AssetSummary(
            $"{category}/Item_{i.ToString("D" + width)}",
            $"Item {i.ToString("D" + width)}",
            category,
            StatusKind.Unknown,
            0,
            $"SCUM/Content/ConZ_Files/Items/{category}/Item_{i.ToString("D" + width)}.uasset",
            null));

    private static AssetSummary Row(string fileName, string name, string category)
        => new($"{category}/{fileName}", name, category, StatusKind.Unknown, 0,
            $"SCUM/Content/ConZ_Files/Items/{category}/{fileName}.uasset", null);

    private sealed class TempDb : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"scmw-asset-index-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            // Pooled connections would keep the file handle open on Windows;
            // clearing pools makes the delete deterministic regardless of the
            // index's own pooling choice.
            SqliteConnection.ClearAllPools();
            try { File.Delete(Path); } catch (IOException) { }
        }
    }
}
