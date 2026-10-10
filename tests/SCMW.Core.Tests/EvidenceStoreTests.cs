// tests/SCMW.Core.Tests/EvidenceStoreTests.cs
// Task 3: the tested-fact store - round-trip, idempotence, corrupt tolerance,
// case-insensitive matching, per-workspace isolation, and honest write failures.
using SCMW.Core.Knowledge;

namespace SCMW.Core.Tests;

public class EvidenceStoreTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static TestedFact Fact(
        string assetId = "item/bow",
        string field = "Damage",
        string? buildId = "0.95",
        string? note = "raised damage, observed in game",
        DateTime? testedUtc = null) =>
        new(assetId, field,
            testedUtc ?? new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc),
            buildId, note);

    [Fact] // the store starts empty and does not create a file until something is recorded
    public void A_fresh_store_is_empty_and_writes_nothing()
    {
        var dir = TempDir();
        var store = new JsonEvidenceStore(dir);

        Assert.Empty(store.All());
        Assert.False(store.HasTestedFact("item/bow", "Damage"));
        Assert.False(File.Exists(Path.Combine(dir, JsonEvidenceStore.FileName)));
    }

    [Fact] // record -> reload round-trips every field
    public void Record_then_reload_round_trips_every_field()
    {
        var dir = TempDir();
        var fact = Fact();

        new JsonEvidenceStore(dir).Record(fact);
        var reloaded = new JsonEvidenceStore(dir);

        Assert.Equal(fact, Assert.Single(reloaded.All()));
        Assert.True(reloaded.HasTestedFact("item/bow", "Damage"));
    }

    [Fact] // the file is camelCase and human-readable
    public void The_store_file_is_camel_case()
    {
        var dir = TempDir();
        new JsonEvidenceStore(dir).Record(Fact());

        var json = File.ReadAllText(Path.Combine(dir, JsonEvidenceStore.FileName));
        Assert.Contains("\"facts\"", json);
        Assert.Contains("\"assetId\"", json);
        Assert.Contains("\"fieldTechnicalName\"", json);
        Assert.Contains("\"testedUtc\"", json);
        Assert.DoesNotContain("\"AssetId\"", json);
        Assert.DoesNotContain("\"FieldTechnicalName\"", json);
    }

    [Fact] // recording the same asset+field twice keeps exactly one fact (first wins)
    public void Recording_the_same_fact_twice_is_idempotent()
    {
        var dir = TempDir();
        var store = new JsonEvidenceStore(dir);

        store.Record(Fact(note: "first"));
        store.Record(Fact(note: "second"));

        Assert.Equal("first", Assert.Single(store.All()).Note);
        Assert.Single(new JsonEvidenceStore(dir).All()); // and only one on disk
    }

    [Fact] // idempotence ignores case, because matching does
    public void Recording_a_differently_cased_duplicate_keeps_one_fact()
    {
        var dir = TempDir();
        var store = new JsonEvidenceStore(dir);

        store.Record(Fact(assetId: "Item/Bow", field: "Damage"));
        store.Record(Fact(assetId: "item/bow", field: "damage"));

        Assert.Single(store.All());
    }

    [Fact] // asset and field matching is case-insensitive
    public void HasTestedFact_matches_asset_and_field_case_insensitively()
    {
        var dir = TempDir();
        var store = new JsonEvidenceStore(dir);
        store.Record(Fact(assetId: "Item/Bow", field: "Damage"));

        Assert.True(store.HasTestedFact("item/bow", "DAMAGE"));
        Assert.True(store.HasTestedFact("ITEM/BOW", "damage"));
    }

    [Fact] // a fact for one asset+field does not upgrade a different field (or asset)
    public void A_fact_does_not_leak_to_another_field_or_asset()
    {
        var dir = TempDir();
        var store = new JsonEvidenceStore(dir);
        store.Record(Fact(assetId: "item/bow", field: "Damage"));

        // Different field on the same asset: still unproven -> Limited, never Editable.
        Assert.False(store.HasTestedFact("item/bow", "Health"));
        Assert.Equal(ResolvedStatus.Limited,
            StatusResolver.Resolve(WriteCapability.Writable, store.HasTestedFact("item/bow", "Health")));

        // Different asset, same field: also unproven.
        Assert.False(store.HasTestedFact("item/axe", "Damage"));

        // Only the exact asset+field earns Editable.
        Assert.Equal(ResolvedStatus.Editable,
            StatusResolver.Resolve(WriteCapability.Writable, store.HasTestedFact("item/bow", "Damage")));
    }

    [Fact] // a corrupt file is "no tested facts yet", never fatal, and heals on next write
    public void A_corrupt_file_is_treated_as_empty_without_throwing()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, JsonEvidenceStore.FileName),
            "{ \"facts\": [ { \"assetId\": 7"); // truncated / wrong shape

        var store = new JsonEvidenceStore(dir); // must not throw

        Assert.Empty(store.All());
        Assert.False(store.HasTestedFact("item/bow", "Damage"));

        store.Record(Fact());
        Assert.Single(new JsonEvidenceStore(dir).All());
    }

    [Fact] // facts are per workspace: two roots never see each other's facts
    public void Two_workspaces_are_isolated()
    {
        var one = TempDir();
        var two = TempDir();

        JsonEvidenceStore.ForWorkspace(one).Record(Fact());

        Assert.Single(JsonEvidenceStore.ForWorkspace(one).All());
        Assert.Empty(JsonEvidenceStore.ForWorkspace(two).All());
        Assert.False(File.Exists(Path.Combine(two, "knowledge", JsonEvidenceStore.FileName)));
    }

    [Fact] // ForWorkspace persists at exactly <root>\knowledge\tested-facts.json
    public void ForWorkspace_writes_into_the_knowledge_folder()
    {
        var root = TempDir();
        JsonEvidenceStore.ForWorkspace(root).Record(Fact());

        Assert.True(File.Exists(Path.Combine(root, "knowledge", JsonEvidenceStore.FileName)));
    }

    [Fact] // atomicity + honesty: a failed write leaves disk and memory unchanged
    public void A_failed_write_keeps_disk_and_memory_unchanged()
    {
        var dir = TempDir();
        var store = new JsonEvidenceStore(dir);
        store.Record(Fact(note: "first"));

        var path = Path.Combine(dir, JsonEvidenceStore.FileName);
        var original = File.ReadAllText(path);

        // Force the atomic replace to fail: a read-only target cannot be overwritten.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            store.Record(Fact(field: "Health", note: "second"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Equal(original, File.ReadAllText(path));                    // previous store intact
        Assert.Single(store.All());                                        // memory not over-claimed
        Assert.False(store.HasTestedFact("item/bow", "Health"));           // the fact was not recorded
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));                    // no temp left behind
    }

    [Fact]
    public void Record_rejects_a_null_fact()
    {
        var store = new JsonEvidenceStore(TempDir());
        Assert.Throws<ArgumentNullException>(() => store.Record(null!));
    }
}
