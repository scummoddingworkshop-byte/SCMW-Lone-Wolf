using SCMW.Core;
using SCMW.Core.Editing;
using SCMW.Core.Logging;

public class ChangeListTests
{
    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-changes-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ChangeRecord Record(
        string assetId = "asset/a",
        string field = "Damage",
        string? oldValue = "1",
        string newValue = "2",
        StatusKind status = StatusKind.Limited,
        string projectId = "p1",
        string backend = "pak",
        string target = "client",
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), projectId, assetId, field, oldValue, newValue,
            new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc), status, backend, target);

    [Fact] // add -> load round-trip preserves every field, value equality included
    public void Add_then_load_round_trips_every_field()
    {
        var folder = TempFolder();
        IChangeStore store = new ProjectChangeStore();
        var record = Record(status: StatusKind.Editable, backend: "uasset", target: "server");

        store.Add(folder, record);

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal(record, loaded);
    }

    [Fact] // backend/target defaults and the StatusKind survive the file round-trip
    public void Defaults_and_status_survive_the_round_trip()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record(status: StatusKind.ReadOnly);

        Assert.Equal("pak", record.Backend);
        Assert.Equal("client", record.Target);

        store.Add(folder, record);

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal("pak", loaded.Backend);
        Assert.Equal("client", loaded.Target);
        Assert.Equal(StatusKind.ReadOnly, loaded.Status);
    }

    [Fact] // the file is camelCase and human-readable (enum as a name, not a number)
    public void Change_file_is_camel_case()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        store.Add(folder, Record(status: StatusKind.Limited));

        var json = File.ReadAllText(Path.Combine(folder, ProjectChangeStore.FileName));
        Assert.Contains("\"records\"", json);
        Assert.Contains("\"fieldTechnicalName\"", json);
        Assert.Contains("\"oldValue\"", json);
        Assert.DoesNotContain("\"FieldTechnicalName\"", json);
        Assert.Contains("\"limited\"", json);
    }

    [Fact] // adding the same Id twice is idempotent: one record, first write wins
    public void Adding_the_same_id_twice_keeps_one_record()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var id = Guid.NewGuid();

        store.Add(folder, Record(id: id, newValue: "first"));
        store.Add(folder, Record(id: id, newValue: "second"));

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal("first", loaded.NewValue);
    }

    [Fact] // undo removes exactly one record and leaves the rest in order
    public void Undo_removes_exactly_one_record_and_preserves_order()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var first = Record(field: "A");
        var middle = Record(field: "B");
        var last = Record(field: "C");
        store.Add(folder, first);
        store.Add(folder, middle);
        store.Add(folder, last);

        store.Undo(folder, middle.Id);

        var remaining = store.Load(folder);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(new[] { first.Id, last.Id }, remaining.Select(r => r.Id).ToArray());
    }

    [Fact] // undoing an unknown id is a no-op, not a corruption
    public void Undo_of_an_unknown_id_changes_nothing()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record();
        store.Add(folder, record);

        store.Undo(folder, Guid.NewGuid());

        Assert.Equal(record, Assert.Single(store.Load(folder)));
    }

    [Fact] // applied value = the latest (last-listed) record; field match is case-insensitive, asset is not
    public void Applied_value_returns_the_latest_record_for_the_asset_and_field()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        store.Add(folder, Record(assetId: "asset/a", field: "Damage", newValue: "10"));
        store.Add(folder, Record(assetId: "asset/a", field: "damage", newValue: "20")); // different case
        store.Add(folder, Record(assetId: "asset/b", field: "Damage", newValue: "999")); // other asset

        Assert.Equal("20", store.AppliedValue(folder, "asset/a", "DAMAGE", "1"));
    }

    [Fact] // no record for the asset+field -> the game value is returned untouched
    public void Applied_value_falls_back_to_the_game_value_when_no_record_matches()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        store.Add(folder, Record(assetId: "asset/a", field: "Damage", newValue: "10"));

        Assert.Equal("7", store.AppliedValue(folder, "asset/b", "Damage", "7")); // other asset
        Assert.Equal("7", store.AppliedValue(folder, "asset/a", "Health", "7")); // other field
    }

    [Fact] // a project with no file at all still answers without throwing
    public void Applied_value_on_an_empty_project_returns_the_game_value()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();

        Assert.Null(store.AppliedValue(folder, "asset/a", "Damage", null));
        Assert.Equal("5", store.AppliedValue(folder, "asset/a", "Damage", "5"));
    }

    [Fact] // after undo, the applied value recomputes back to the game value
    public void Applied_value_recomputes_to_the_game_value_after_undo()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        var record = Record(assetId: "asset/a", field: "Damage", newValue: "50");

        store.Add(folder, record);
        Assert.Equal("50", store.AppliedValue(folder, "asset/a", "Damage", "5"));

        store.Undo(folder, record.Id);
        Assert.Equal("5", store.AppliedValue(folder, "asset/a", "Damage", "5"));
    }

    [Fact] // Review Focus 3: two projects never see each other's changes
    public void Two_projects_are_isolated()
    {
        var one = TempFolder();
        var two = TempFolder();
        var store = new ProjectChangeStore();

        store.Add(one, Record(projectId: "p1", newValue: "one"));

        Assert.Single(store.Load(one));
        Assert.Empty(store.Load(two));
        Assert.False(File.Exists(Path.Combine(two, ProjectChangeStore.FileName)));

        store.Add(two, Record(projectId: "p2", newValue: "two"));

        Assert.Equal("one", Assert.Single(store.Load(one)).NewValue);
        Assert.Equal("two", Assert.Single(store.Load(two)).NewValue);
    }

    [Fact] // a corrupt/partial changes.json is empty, not fatal, and heals on the next write
    public void Corrupt_change_file_is_treated_as_empty_without_throwing()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        File.WriteAllText(Path.Combine(folder, ProjectChangeStore.FileName),
            "{ \"records\": [ { \"id\": \"not-a-guid\"");

        var loaded = store.Load(folder); // must not throw

        Assert.Empty(loaded);

        var record = Record();
        store.Add(folder, record);
        Assert.Equal(record, Assert.Single(store.Load(folder)));
    }

    [Fact] // the corrupt read is reported through the plain warning log
    public void Corrupt_change_file_logs_a_plain_warning()
    {
        var logs = Path.Combine(Path.GetTempPath(), "scmw-changes-log-" + Guid.NewGuid());
        Log.SetLogsDir(logs);
        var folder = TempFolder();
        File.WriteAllText(Path.Combine(folder, ProjectChangeStore.FileName), "not json at all");

        new ProjectChangeStore().Load(folder);

        var text = string.Concat(Directory.GetFiles(logs).Select(File.ReadAllText));
        Assert.Contains("change list", text);
    }

    [Fact] // a null/blank project folder is rejected up-front: never an unhandled exception
    public void A_null_or_blank_project_folder_never_throws()
    {
        IChangeStore store = new ProjectChangeStore();
        var record = Record();

        foreach (string? folder in new string?[] { null, "", "   " })
        {
            Assert.Empty(store.Load(folder!));
            store.Add(folder!, record);              // no-op, never throws
            store.Undo(folder!, record.Id);          // no-op, never throws
            Assert.Equal("game", store.AppliedValue(folder!, "a", "f", "game"));
            Assert.Empty(store.ListVersions(folder!));

            var version = store.SaveVersion(folder!, "label"); // version-shaped no-op
            Assert.NotNull(version);
            Assert.False(string.IsNullOrWhiteSpace(store.Restore(folder!, version.Id)));
        }
    }

    [Fact] // atomicity: a failed write leaves the previous file exactly as it was, with no temp left
    public void A_failed_write_leaves_the_previous_file_intact()
    {
        var folder = TempFolder();
        var store = new ProjectChangeStore();
        store.Add(folder, Record(newValue: "first"));

        var path = Path.Combine(folder, ProjectChangeStore.FileName);
        var original = File.ReadAllText(path);

        // Force the atomic replace to fail: a read-only target cannot be overwritten.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            store.Add(folder, Record(newValue: "second"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Equal(original, File.ReadAllText(path));                 // previous list intact
        Assert.Equal("first", Assert.Single(store.Load(folder)).NewValue);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));              // no temp file left behind
    }
}
