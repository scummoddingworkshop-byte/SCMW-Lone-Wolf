using SCMW.Core;
using SCMW.Core.Editing;

// Task 5 (Plan 3): the project's manual versions. A version is a timestamped snapshot of the
// change list, saved beside changes.json; restoring one never loses the current list because an
// automatic snapshot is taken first. Every failure is a plain message, never a crash.
public class ProjectVersionTests
{
    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-versions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ChangeRecord Record(string field = "Damage", string newValue = "2",
        string? oldValue = "1", string assetId = "asset/a", Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "p1", assetId, field, oldValue, newValue,
            new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc), StatusKind.Limited);

    [Fact] // save -> list -> restore round-trips the change list exactly
    public void Save_then_list_then_restore_round_trips_the_change_list_exactly()
    {
        var folder = TempFolder();
        IChangeStore store = new ProjectChangeStore();
        var first = Record(field: "Damage", newValue: "42");
        var second = Record(field: "Weight", newValue: "9");
        store.Add(folder, first);
        store.Add(folder, second);

        var version = store.SaveVersion(folder, "After tuning");

        Assert.Equal("After tuning", version.Label);
        Assert.Equal(2, version.ChangeCount);

        var listed = Assert.Single(store.ListVersions(folder));
        Assert.Equal(version.Id, listed.Id);
        Assert.Equal("After tuning", listed.Label);
        Assert.Equal(2, listed.ChangeCount);

        // Change the current list, then restore the version.
        store.Undo(folder, second.Id);
        store.Add(folder, Record(field: "Health", newValue: "100"));
        Assert.Equal(2, store.Load(folder).Count);

        var error = store.Restore(folder, version.Id);

        Assert.Null(error);
        var restored = store.Load(folder);
        Assert.Equal(new[] { first, second }, restored);
    }

    [Fact] // restoring auto-snapshots the pre-restore state so nothing is ever lost
    public void Restore_auto_snapshots_the_pre_restore_state()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var first = Record(field: "Damage", newValue: "42");
        var second = Record(field: "Weight", newValue: "9");
        store.Add(folder, first);
        store.Add(folder, second);

        var version = store.SaveVersion(folder, "Two changes");

        // The current list now differs from the version (one record removed).
        store.Undo(folder, first.Id);
        var preRestore = store.Load(folder);
        Assert.Single(preRestore);

        Assert.Null(store.Restore(folder, version.Id));

        // The automatic snapshot exists, is labelled, and holds the pre-restore list exactly.
        var snapshot = store.ListVersions(folder).Single(v => v.Label == ProjectChangeStore.AutoSnapshotLabel);
        Assert.Equal(preRestore.Count, snapshot.ChangeCount);

        // Restoring the auto-snapshot proves its contents are the pre-restore list.
        Assert.Null(store.Restore(folder, snapshot.Id));
        Assert.Equal(preRestore, store.Load(folder));
    }

    [Fact] // a corrupt version file is reported plainly without throwing, and changes nothing
    public void A_corrupt_version_file_is_reported_plainly_without_throwing()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record(newValue: "7");
        store.Add(folder, record);
        var version = store.SaveVersion(folder, "oops");

        var path = Path.Combine(folder, ProjectChangeStore.VersionsFolderName, version.Id + ".json");
        File.WriteAllText(path, "{ \"label\": \"oops\", \"records\": [ { \"id\": \"not-a-guid\"");

        var error = store.Restore(folder, version.Id); // must not throw

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(new[] { record }, store.Load(folder));           // current list unchanged
        Assert.DoesNotContain(store.ListVersions(folder),            // no auto-snapshot was taken
            v => v.Label == ProjectChangeStore.AutoSnapshotLabel);
    }

    [Fact] // restoring an unknown id returns a plain message and changes nothing
    public void Restoring_an_unknown_id_returns_a_plain_message_and_changes_nothing()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record(newValue: "5");
        store.Add(folder, record);

        var error = store.Restore(folder, "20990101-000000");

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(new[] { record }, store.Load(folder));
        Assert.Empty(store.ListVersions(folder));
    }

    [Fact] // a label is optional; an unnamed version still round-trips
    public void Save_without_a_label_still_saves_a_listable_version()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        store.Add(folder, Record());

        var version = store.SaveVersion(folder, null);

        Assert.Equal(string.Empty, version.Label);
        var listed = Assert.Single(store.ListVersions(folder));
        Assert.Equal(string.Empty, listed.Label);
        Assert.Equal(1, listed.ChangeCount);
        Assert.Equal(version.Id, listed.Id);
    }

    [Fact] // two saves in the same second get distinct files, and the list is newest-first
    public void Versions_are_unique_and_listed_newest_first()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();

        var older = store.SaveVersion(folder, "first");
        var newer = store.SaveVersion(folder, "second");

        Assert.NotEqual(older.Id, newer.Id);
        Assert.True(File.Exists(Path.Combine(folder, ProjectChangeStore.VersionsFolderName, older.Id + ".json")));
        Assert.True(File.Exists(Path.Combine(folder, ProjectChangeStore.VersionsFolderName, newer.Id + ".json")));

        var listed = store.ListVersions(folder);
        Assert.Equal(2, listed.Count);
        Assert.Equal(newer.Id, listed[0].Id); // newest first
    }

    [Fact] // an empty version folder is "no versions", never a throw
    public void List_versions_on_a_project_without_versions_is_empty()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();

        Assert.Empty(store.ListVersions(folder));
    }

    [Fact] // a version file with NO records field is corrupt: refuse, change nothing, no snapshot
    public void A_version_missing_the_records_field_is_refused_and_changes_nothing()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record(newValue: "42");
        store.Add(folder, record);

        var versionsDir = Path.Combine(folder, ProjectChangeStore.VersionsFolderName);
        Directory.CreateDirectory(versionsDir);
        var id = "20260927-120000";
        File.WriteAllText(Path.Combine(versionsDir, id + ".json"),
            "{ \"label\": \"hand-written\", \"createdUtc\": \"2026-09-27T12:00:00Z\" }");

        var error = store.Restore(folder, id); // must not throw

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(new[] { record }, store.Load(folder));           // current list unchanged
        Assert.DoesNotContain(store.ListVersions(folder),            // no auto-snapshot was taken
            v => v.Label == ProjectChangeStore.AutoSnapshotLabel);
    }

    [Fact] // an explicit empty snapshot is legitimate: it restores to an empty list (with a backup)
    public void An_explicit_empty_version_restores_to_an_empty_list()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record(newValue: "42");
        store.Add(folder, record);

        var versionsDir = Path.Combine(folder, ProjectChangeStore.VersionsFolderName);
        Directory.CreateDirectory(versionsDir);
        var id = "20260927-120000";
        File.WriteAllText(Path.Combine(versionsDir, id + ".json"),
            "{ \"label\": \"hand-written\", \"createdUtc\": \"2026-09-27T12:00:00Z\", \"records\": [] }");

        var error = store.Restore(folder, id); // must not throw

        Assert.Null(error);
        Assert.Empty(store.Load(folder));                            // the change list is now empty
        Assert.Contains(store.ListVersions(folder),                  // the pre-restore list was backed up
            v => v.Label == ProjectChangeStore.AutoSnapshotLabel);
    }
}
