using SCMW.Core.Editing;

public class CraftingSectionStoreTests
{
    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-sections-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static CraftingSection Section(Guid? id = null, string name = "SCMW Mods",
        string tag = "CraftingCategory.Items.SCMW_Mods", DateTime? createdUtc = null) =>
        new(id ?? Guid.NewGuid(), name, tag, createdUtc ?? new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc));

    [Fact] // add -> load keeps every field, in order
    public void Add_then_load_round_trips_every_field()
    {
        var folder = TempFolder();
        ICraftingSectionStore store = new CraftingSectionStore();
        var section = Section();

        Assert.True(store.Add(folder, section));

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal(section, loaded);
    }

    [Fact] // the same Id twice keeps one section; the first write wins
    public void Adding_the_same_id_twice_keeps_one_section()
    {
        var folder = TempFolder();
        var store = new CraftingSectionStore();
        var id = Guid.NewGuid();

        store.Add(folder, Section(id: id, name: "First"));
        store.Add(folder, Section(id: id, name: "Second"));

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal("First", loaded.Name);
    }

    [Fact] // undo removes exactly one section and keeps the others in order
    public void Undo_removes_exactly_one_section_and_preserves_order()
    {
        var folder = TempFolder();
        var store = new CraftingSectionStore();
        var first = Section(name: "One");
        var middle = Section(name: "Two");
        var last = Section(name: "Three");
        store.Add(folder, first);
        store.Add(folder, middle);
        store.Add(folder, last);

        store.Undo(folder, middle.Id);

        var remaining = store.Load(folder);
        Assert.Equal(new[] { first.Id, last.Id }, remaining.Select(s => s.Id).ToArray());
    }

    [Fact] // a damaged file is kept aside rather than silently overwritten
    public void A_damaged_file_is_kept_aside_and_read_as_empty()
    {
        var folder = TempFolder();
        var store = new CraftingSectionStore();
        File.WriteAllText(Path.Combine(folder, CraftingSectionStore.FileName), "{ not json");

        var loaded = store.Load(folder);

        Assert.Empty(loaded);
        Assert.NotEmpty(Directory.GetFiles(folder, "crafting-sections.corrupt-*.json"));
    }

    [Fact] // a blank folder is refused, never throws
    public void A_blank_folder_is_refused()
    {
        var store = new CraftingSectionStore();

        Assert.False(store.Add("", Section()));
        Assert.Empty(store.Load(""));
    }
}
