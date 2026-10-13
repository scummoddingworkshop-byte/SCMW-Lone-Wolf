using CUE4Parse.UE4.Assets.Exports;
using Microsoft.Data.Sqlite;
using SCMW.Backend.Pak;
using SCMW.Core;
using SCMW.Core.Content;
using Xunit;

public class PakContentSourceTests
{
    private const string ItemRoot = "SCUM/Content/ConZ_Files/Items/";

    private static PakContentSource CreateSource(FakePakMount mount)
        => new(mount);

    private static string Item(string relative)
        => ItemRoot + relative;

    [Fact]
    public void Configure_bad_path_sets_error_without_throwing()
    {
        // A real directory so the missing-folder short-circuit does not apply and
        // the call reaches TryMount, which reports failure (the test's original intent).
        var dir = Directory.CreateTempSubdirectory("scmw-paks-");
        try
        {
            var mount = new FakePakMount { MountSucceeds = false, MountError = "Pak path not found." };
            var src = CreateSource(mount);

            // Must not throw; must surface a plain Error message.
            src.Configure(dir.FullName, "dummy-key", null);

            Assert.Equal(ContentSourceState.Error, src.Status.State);
            Assert.False(string.IsNullOrWhiteSpace(src.Status.Message));
            Assert.DoesNotContain('\n', src.Status.Message); // plain, no stack trace
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); }
            catch (IOException) { } // best-effort cleanup
        }
    }

    [Fact]
    public void Configure_missing_paks_dir_reports_plain_error_without_raw_path()
    {
        var src = CreateSource(new FakePakMount());

        // A path that does not exist (with a key present) -> the honest "not found"
        // Error, with a plain message and no echo of the raw path.
        src.Configure(@"Z:\does\not\exist\Content\Paks", "dummy-key", null);

        Assert.Equal(ContentSourceState.Error, src.Status.State);
        Assert.Equal(
            "SCUM's Content\\Paks folder wasn't found under your game path — re-detect it in Settings.",
            src.Status.Message);
        Assert.DoesNotContain("Z:", src.Status.Message);
    }

    [Fact]
    public void Configure_missing_key_sets_no_key()
    {
        var src = CreateSource(new FakePakMount());

        // A real directory so the path check passes and the key check is reached.
        var dir = Directory.CreateTempSubdirectory("scmw-paks-");
        try
        {
            src.Configure(dir.FullName, null, null);
            Assert.Equal(ContentSourceState.NoKey, src.Status.State);
            Assert.Contains("key", src.Status.Message, StringComparison.OrdinalIgnoreCase);

            src.Configure(dir.FullName, "   ", null);
            Assert.Equal(ContentSourceState.NoKey, src.Status.State);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); }
            catch (IOException) { } // best-effort cleanup
        }
    }

    [Fact]
    public void Configure_invalid_nonempty_path_with_no_key_reports_path_error_not_no_key()
    {
        var src = CreateSource(new FakePakMount());

        // Path validation wins over the missing-key check: a nonempty invalid path with
        // no key must report the path problem, not the misleading NoKey state.
        src.Configure(@"Z:\does\not\exist\Content\Paks", null, null);

        Assert.Equal(ContentSourceState.Error, src.Status.State);
        Assert.Equal(
            "SCUM's Content\\Paks folder wasn't found under your game path — re-detect it in Settings.",
            src.Status.Message);
    }

    [Fact]
    public void Configure_missing_path_sets_not_configured()
    {
        var src = CreateSource(new FakePakMount());

        src.Configure("", "dummy-key", null);
        Assert.Equal(ContentSourceState.NotConfigured, src.Status.State);

        src.Configure(null!, "dummy-key", null);
        Assert.Equal(ContentSourceState.NotConfigured, src.Status.State);
    }

    [Fact]
    public async Task Item_name_is_derived_from_filename()
    {
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        await src.ScanAsync();
        var results = await src.SearchAsync("", null);

        var bow = Assert.Single(results);
        Assert.Equal("Improvised Bow", bow.Name);
        Assert.Equal("Weapons", bow.Category);
        Assert.Equal(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"), bow.TechnicalPath);
        Assert.Equal(Item("Weapons/Ranged_Weapons/Improvised_Bow"), bow.Id);
        Assert.Equal(StatusKind.Unknown, bow.Overall);
        Assert.Equal(0, bow.EditableCount);
        Assert.Null(bow.IconKey);
    }

    [Fact]
    public async Task Scan_filters_non_item_files_and_discovers_categories()
    {
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));   // item
        mount.Paths.Add(Item("Food/Canned_Food/CannedSpaghetti.uasset"));        // item
        mount.Paths.Add(Item("Item_Icons/ICO_Improvised_Bow.uasset"));           // excluded subtree
        mount.Paths.Add(Item("Weapons/BP_SomeManager.uasset"));                  // excluded prefix
        mount.Paths.Add(Item("Equipment/SM_Prop.uasset"));                       // excluded prefix
        mount.Paths.Add(Item("Tags/WeaponTags.uasset"));                         // excluded subtree
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Recurve_Bow.uexp"));        // not .uasset
        mount.Paths.Add("Engine/Content/NotAnItem.uasset");                      // outside Items
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        await src.ScanAsync();

        Assert.Equal(2, (await src.SearchAsync("", null)).Count);
        Assert.Equal(new[] { "Food", "Weapons" }, src.Categories);
    }

    [Fact]
    public async Task SearchAsync_filters_by_category_and_query()
    {
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        mount.Paths.Add(Item("Food/Canned_Food/CannedSpaghetti.uasset"));
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        await src.ScanAsync();

        var weapons = await src.SearchAsync("", "Weapons");
        Assert.Single(weapons);
        Assert.Equal("Improvised Bow", weapons[0].Name);

        var byQuery = await src.SearchAsync("spaghetti", null);
        var match = Assert.Single(byQuery);
        Assert.Equal("CannedSpaghetti", match.Name);

        Assert.Empty(await src.SearchAsync("", "Drink"));
    }

    [Fact]
    public async Task SearchAsync_respects_skip_and_take()
    {
        var mount = new FakePakMount();
        for (var i = 0; i < 30; i++)
            mount.Paths.Add(Item($"Equipment/Item_{i:D4}.uasset"));
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        await src.ScanAsync();

        var page = await src.SearchAsync("", null, skip: 10, take: 5);
        Assert.Equal(5, page.Count);
        Assert.Equal("Item 0010", page[0].Name);
        Assert.Equal("Item 0014", page[^1].Name);

        var tail = await src.SearchAsync("", null, skip: 28, take: 5);
        Assert.Equal(2, tail.Count);
    }

    [Fact]
    public async Task SearchAsync_caps_take()
    {
        var mount = new FakePakMount();
        for (var i = 0; i < 600; i++)
            mount.Paths.Add(Item($"Equipment/Item_{i:D4}.uasset"));
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        await src.ScanAsync();

        var capped = await src.SearchAsync("", null, skip: 0, take: 100_000);
        Assert.Equal(500, capped.Count);
    }

    // Requirement 5: the SQLite index (not the in-memory list) is the source of
    // truth for searches. With a temp db + the fake mount, a scan persists rows;
    // a search returns them; then editing the db directly (bypassing the source)
    // changes what the search returns — proving SearchAsync reads the index.
    [Fact]
    public async Task Index_is_the_search_path_not_the_in_memory_list()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        mount.Paths.Add(Item("Food/Canned_Food/CannedSpaghetti.uasset"));
        var src = new PakContentSource(mount, index, db.Path);

        src.Configure(Path.GetTempPath(), "dummy-key", "build-42");
        var st = await src.ScanAsync();
        Assert.Equal(ContentSourceState.Ready, st.State);

        // The scan persisted the rows (and the build id) to the db.
        Assert.Equal(2, index.Count(db.Path));
        Assert.Equal("build-42", index.ReadBuildId(db.Path));

        // Search is served from the index.
        var bow = await src.SearchAsync("bow", null);
        Assert.Single(bow);
        Assert.Equal("Improvised Bow", bow[0].Name);

        // Prove the db is the source of truth: replace its contents directly,
        // bypassing the source's in-memory state; the search must follow the db.
        index.ReplaceAll(db.Path, "build-42", new[]
        {
            new AssetSummary("Weapons/Only_Changed_Item", "Only Changed Item", "Weapons",
                StatusKind.Unknown, 0, Item("Weapons/Only_Changed_Item.uasset"), null),
        });

        var changed = await src.SearchAsync("", null);
        Assert.Single(changed);
        Assert.Equal("Only Changed Item", changed[0].Name);
        Assert.Empty(await src.SearchAsync("bow", null)); // old rows are gone
    }

    // Requirement 3: Configure (run at startup) must not scan/persist, and the
    // staleness decision is deferred to ScanAsync. Only ScanAsync rebuilds.
    [Fact]
    public async Task Configure_does_not_scan_and_scan_rebuilds_stale_index()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        var src = new PakContentSource(mount, index, db.Path);

        src.Configure(Path.GetTempPath(), "dummy-key", "build-42");

        // Startup path: no enumeration, no rows persisted, index is still stale.
        Assert.Equal(0, index.Count(db.Path));
        Assert.True(index.IsStale(db.Path, "build-42"));

        await src.ScanAsync();
        Assert.Equal(1, index.Count(db.Path));
        Assert.False(index.IsStale(db.Path, "build-42"));

        // A second scan on the same build id skips the enumeration (no error,
        // index unchanged and still authoritative).
        await src.ScanAsync();
        Assert.Equal(1, index.Count(db.Path));
    }

    [Fact]
    public async Task A_duplicate_file_path_is_indexed_once_and_never_breaks_the_scan()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        var mount = new FakePakMount();
        // Layered paks can hand the same file path to the reader twice (seen on the
        // owner's install: 23 item paths listed twice). The index must not choke.
        mount.Paths.Add(Item("Equipment/Test_Paper_ES.uasset"));
        mount.Paths.Add(Item("Equipment/Test_Paper_ES.uasset"));
        var src = new PakContentSource(mount, index, db.Path);

        src.Configure(Path.GetTempPath(), "dummy-key", "build-42");
        var status = await src.ScanAsync();

        Assert.Equal(ContentSourceState.Ready, status.State);
        Assert.Equal(1, index.Count(db.Path));
        Assert.Single(await src.SearchAsync("", null));
    }

    [Fact]
    public async Task LastScan_reports_nothing_until_scanned_then_time_count_and_build()
    {
        using var db = new TempDb();
        var index = new AssetIndex();
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        var src = new PakContentSource(mount, index, db.Path);

        // No db scan yet: LastScan carries no timestamp.
        var before = src.LastScan;
        Assert.NotNull(before);
        Assert.Null(before!.ScannedAtUtc);

        src.Configure(Path.GetTempPath(), "dummy-key", "build-42");
        await src.ScanAsync();

        var after = src.LastScan!;
        Assert.NotNull(after.ScannedAtUtc);
        Assert.Equal(1, after.ItemCount);
        Assert.Equal("build-42", after.BuildId);
    }

    [Fact]
    public async Task FieldsAsync_returns_empty_when_not_mounted()
    {
        var mount = new FakePakMount { MountSucceeds = false, MountError = "nope" };
        var src = CreateSource(mount);
        src.Configure(@"Z:\nope", "dummy-key", null);

        Assert.Empty(await src.FieldsAsync("anything"));
    }

    [Fact]
    public void Configure_mount_throw_surfaces_curated_plain_error()
    {
        // Exercises the real catch path in Configure via the seam: a mount
        // that breaks IPakMount's never-throws contract must still yield a
        // curated Error status — no raw exception text may reach the user.
        // A real directory so the missing-folder short-circuit does not apply.
        var dir = Directory.CreateTempSubdirectory("scmw-paks-");
        try
        {
            var mount = new FakePakMount { ThrowOnMount = new InvalidOperationException("boom") };
            var src = CreateSource(mount);

            src.Configure(dir.FullName, "dummy-key", null);

            Assert.Equal(ContentSourceState.Error, src.Status.State);
            Assert.Equal(
                "SCUM's files could not be read from that folder. Check the path in Settings, then try again.",
                src.Status.Message);
            Assert.DoesNotContain("boom", src.Status.Message);
            Assert.DoesNotContain("InvalidOperationException", src.Status.Message);
            Assert.DoesNotContain('\n', src.Status.Message); // plain, no stack trace
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); }
            catch (IOException) { } // best-effort cleanup
        }
    }

    [Fact]
    public async Task Scan_probes_an_item_and_reports_error_when_the_key_cannot_decrypt()
    {
        // A rotated/invalid key still enumerates paths (FilePaths visible) but cannot
        // read the item packages. The probe must turn that into a key Error — never
        // Ready with unreadable items.
        var mount = new FakePakMount { LoadSucceeds = false };
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        var status = await src.ScanAsync();

        Assert.Equal(ContentSourceState.Error, status.State);
        Assert.Equal(
            "SCUM's files couldn't be decrypted — your AES key may be missing, wrong, or outdated. Check it in Settings.",
            status.Message);
        Assert.DoesNotContain('\n', status.Message);       // plain, no raw exception text
        Assert.DoesNotContain("Exception", status.Message);
    }

    [Fact]
    public async Task Scan_succeeds_when_the_first_candidate_is_a_false_positive_but_later_candidates_load()
    {
        // The real install's items[0] (Ammunition/12_Gauge_BirdShot) is a heuristic
        // FALSE POSITIVE: its class/CDO does not load even with the correct key, while
        // candidates 1..N load fine. The probe must sample more than items[0] and report
        // Ready when a later candidate decrypts — never "wrong key" on a correct key.
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Ammunition/12_Gauge_BirdShot.uasset"));       // index 0: false positive
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        mount.Paths.Add(Item("Food/Canned_Food/CannedSpaghetti.uasset"));
        var src = CreateSource(mount);

        // Only the false positive's CDO fails to load; every other candidate succeeds.
        mount.LoadResult = path =>
            !path.StartsWith(Item("Ammunition/12_Gauge_BirdShot"), StringComparison.OrdinalIgnoreCase);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        var status = await src.ScanAsync();

        Assert.Equal(ContentSourceState.Ready, status.State);
    }

    [Fact]
    public async Task Scan_returns_ready_when_no_candidates_are_enumerated()
    {
        // An empty enumeration (no paths match the item filter) has nothing to probe
        // and must not be reported as a wrong key — Ready with zero items (unchanged).
        var mount = new FakePakMount();
        var src = CreateSource(mount);

        src.Configure(Path.GetTempPath(), "dummy-key", null);
        var status = await src.ScanAsync();

        Assert.Equal(ContentSourceState.Ready, status.State);
        Assert.Empty(await src.SearchAsync("", null));
    }

    [Fact] // Final review: Clear must never race an in-flight extraction pass
    public async Task ClearIcons_is_queued_behind_a_running_pass_and_then_empties_the_cache()
    {
        var dir = Directory.CreateTempSubdirectory("scmw-clear-");
        var mount = new FakePakMount { BlockLoad = true, LoadSucceeds = false };
        mount.LoadStarted = new TaskCompletionSource();
        try
        {
            var cache = new FileIconCache(dir.FullName);
            var existing = Path.Combine(dir.FullName, "existing.png");
            File.WriteAllBytes(existing, new byte[] { 1, 2, 3 });

            mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
            var src = new PakContentSource(mount, null, null, cache);
            src.Configure(Path.GetTempPath(), "dummy-key", null);

            var pass = src.ExtractIconsAsync(new[] { Item("Weapons/Ranged_Weapons/Improvised_Bow") });
            await mount.LoadStarted.Task; // the pass is now mid-load, holding the extraction gate

            var clear = src.ClearIconsAsync();
            await Task.Delay(100);

            // Queued: while the pass holds the gate the clear can neither delete nor
            // complete, so a partially repopulated cache is impossible.
            Assert.False(clear.IsCompleted);
            Assert.True(File.Exists(existing));

            mount.ReleaseLoad();
            await clear;
            Assert.False(File.Exists(existing)); // emptied only once the pass unwound

            // The cancelled pass ends without corrupting the cache.
            try { await pass; }
            catch (OperationCanceledException) { }
        }
        finally
        {
            mount.ReleaseLoad(); // release a possibly-still-blocked load so the thread ends
            try { Directory.Delete(dir.FullName, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void A_recipe_is_indexed_with_its_plain_name_so_the_build_can_resolve_it()
    {
        // The build resolves an asset by the exact name the index holds, so the index must name a
        // recipe the same way the Recipes screen shows it.
        Assert.True(PakContentSource.TryMapItem(
            Item("Crafting/Recipes/Placeables/CR_Base_Garden.uasset"), out var recipe));
        Assert.Equal("Base Garden", recipe.Name);

        Assert.True(PakContentSource.TryMapItem(
            Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"), out var bow));
        Assert.Equal("Improvised Bow", bow.Name);
    }

    // --- Loot & spawn: a third "place to look" (Task 2.6) ----------------------------------------

    [Fact]
    public void A_spawner_preset_is_indexed_from_its_own_folders()
    {
        // The two Items/ spawner folders are skipped as items (see ExcludedSubfolders) but read
        // here as presets; the fish/animal presets live under Characters/.
        Assert.True(PakContentSource.TryMapSpawnerPreset(
            "SCUM/Content/ConZ_Files/Items/SpawnerPresets2/Buildings/Warehouse/Examine_Crate_Brown_Big.uasset",
            out var crate));
        Assert.Equal("Examine Crate Brown Big", crate.Name);
        Assert.Equal("Buildings", crate.Category);

        Assert.True(PakContentSource.TryMapSpawnerPreset(
            "SCUM/Content/ConZ_Files/Characters/SpawnerPresets/FishSpeciesPresets/SpeciesPreset_River_Big.uasset",
            out var fish));
        Assert.Equal("SpeciesPreset River Big", fish.Name);
        Assert.Equal("FishSpeciesPresets", fish.Category);

        // A plain item is not a spawner preset.
        Assert.False(PakContentSource.TryMapSpawnerPreset(
            Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"), out _));
    }

    [Fact]
    public void A_spawner_preset_is_still_not_counted_as_an_item()
    {
        // Guard: the loot folders stay excluded from the item list even though they are now
        // indexed as presets, so the Items screen cannot drift.
        Assert.False(PakContentSource.TryMapItem(
            "SCUM/Content/ConZ_Files/Items/SpawnerPresets2/Buildings/Warehouse/Examine_Crate_Brown_Big.uasset",
            out _));
    }

    [Fact]
    public async Task A_scan_indexes_spawner_presets_alongside_items()
    {
        var mount = new FakePakMount();
        mount.Paths.Add(Item("Weapons/Ranged_Weapons/Improvised_Bow.uasset"));
        mount.Paths.Add("SCUM/Content/ConZ_Files/Items/SpawnerPresets2/Buildings/Warehouse/Examine_Crate_Brown_Big.uasset");
        mount.Paths.Add("SCUM/Content/ConZ_Files/Characters/SpawnerPresets/FishSpeciesPresets/SpeciesPreset_River_Big.uasset");

        var src = CreateSource(mount);
        src.Configure(Path.GetTempPath(), "dummy-key", null);
        var status = await src.ScanAsync();

        Assert.Equal(ContentSourceState.Ready, status.State);

        var all = await src.SearchAsync("", null, 0, 100);
        Assert.Contains(all, a => a.TechnicalPath.EndsWith("Improvised_Bow.uasset"));
        Assert.Contains(all, a => a.TechnicalPath.EndsWith("Examine_Crate_Brown_Big.uasset"));
        Assert.Contains(all, a => a.TechnicalPath.EndsWith("SpeciesPreset_River_Big.uasset"));
    }

    private sealed class TempDb : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"scmw-paksrc-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(Path); } catch (IOException) { }
        }
    }

    private sealed class FakePakMount : IPakMount
    {
        public List<string> Paths { get; } = new();

        public bool MountSucceeds { get; set; } = true;

        public string? MountError { get; set; }

        /// <summary>When set, TryMount throws it instead of returning (tests the never-throws breach).</summary>
        public Exception? ThrowOnMount { get; set; }

        public bool IsMounted { get; private set; }

        public int FileCount => Paths.Count;

        public IEnumerable<string> FilePaths => Paths;

        /// <summary>When true, <see cref="TryLoadPackageObject"/> pauses until <see cref="ReleaseLoad"/>.</summary>
        public bool BlockLoad { get; set; }

        /// <summary>Signalled once a blocking load has started (the pass now holds the extraction gate).</summary>
        public TaskCompletionSource? LoadStarted { get; set; }

        private readonly TaskCompletionSource _loadRelease = new();

        public void ReleaseLoad() => _loadRelease.TrySetResult();

        public bool TryMount(string pakPath, string? aesKey, out string? error)
        {
            if (ThrowOnMount is not null)
                throw ThrowOnMount;

            error = MountError;
            IsMounted = MountSucceeds;
            return MountSucceeds;
        }

        public bool TryLoadPackageObject(string path, out UObject? obj)
        {
            obj = null;

            if (BlockLoad)
            {
                LoadStarted?.TrySetResult();
                _loadRelease.Task.Wait();
            }

            return LoadResult?.Invoke(path) ?? LoadSucceeds;
        }

        /// <summary>
        /// When set, decides success per CDO/package path (simulates heuristic false
        /// positives: some item paths enumerate but their class/CDO does not load even
        /// with a correct key). When null, the blanket <see cref="LoadSucceeds"/> applies.
        /// </summary>
        public Func<string, bool>? LoadResult { get; set; }

        /// <summary>
        /// When false, every CDO/package load fails (simulates a rotated/invalid key that
        /// still mounts and enumerates paths but cannot decrypt the item packages).
        /// </summary>
        public bool LoadSucceeds { get; set; } = true;

        public void Dispose() { }
    }
}
