using Avalonia.Headless.XUnit;
using SCMW.App;

namespace SCMW.App.Tests;

/// <summary>
/// Keeping a copy of the player's saved game (SCUM.db) and putting one back. Snapshots live in the
/// SCMW workspace; restoring keeps the current save first, so a restore can never lose it.
/// </summary>
public class SaveBackupsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-save-" + Guid.NewGuid().ToString("N"));
    private readonly string _save;

    public SaveBackupsServiceTests()
    {
        Directory.CreateDirectory(_root);
        _save = Path.Combine(_root, "SaveFiles");
        Directory.CreateDirectory(_save);
        File.WriteAllText(Path.Combine(_save, "SCUM.db"), "current save");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string SavesRoot => Path.Combine(_root, "Backups", "saves");
    private string Current => Path.Combine(_save, "SCUM.db");
    private SaveBackupsService New() => new(_save, _root);

    [AvaloniaFact]
    public void Backing_up_copies_the_current_save_into_the_workspace()
    {
        var service = New();

        Assert.Null(service.BackUp());

        var one = Assert.Single(service.List());
        Assert.Equal("current save", File.ReadAllText(one.Path));
        Assert.StartsWith(SavesRoot, one.Path);
    }

    [AvaloniaFact]
    public void Restoring_puts_the_chosen_save_back_and_keeps_the_current_one_first()
    {
        var service = New();
        service.BackUp();                       // a copy of "current save"

        File.WriteAllText(Current, "changed save");   // the player then plays on
        File.WriteAllText(Current + "-wal", "stale"); // a leftover journal from an unclean exit

        var snapshot = service.List().Single();
        Assert.Null(service.Restore(snapshot.Id));

        Assert.Equal("current save", File.ReadAllText(Current));   // the copy is back
        Assert.False(File.Exists(Current + "-wal"));               // no stale journal left behind
        Assert.Equal(2, service.List().Count);                     // "changed save" was kept first
    }

    [AvaloniaFact]
    public void Restoring_an_unknown_copy_is_refused()
    {
        var service = New();
        Assert.NotNull(service.Restore("nope"));
    }

    [AvaloniaFact]
    public void Backing_up_with_no_save_present_is_refused_plainly()
    {
        File.Delete(Current);
        var service = New();

        Assert.False(service.IsAvailable);
        Assert.NotNull(service.BackUp());
        Assert.Empty(service.List());
    }

    [AvaloniaFact]
    public void Only_the_newest_few_save_copies_are_kept()
    {
        Directory.CreateDirectory(SavesRoot);
        for (var i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(SavesRoot, $"SCUM_2026100{i}-101112.db"), "x");

        var saves = New().List();

        Assert.Equal(3, saves.Count);                            // the newest three...
        Assert.Equal(3, Directory.GetFiles(SavesRoot).Length);   // ...and the older two are gone
        Assert.DoesNotContain(saves, s => s.Id.Contains("20261001"));
        Assert.DoesNotContain(saves, s => s.Id.Contains("20261002"));
    }

    [AvaloniaFact]
    public void Deleting_a_kept_save_removes_it()
    {
        var service = New();
        service.BackUp();

        var one = service.List().Single();
        Assert.Null(service.Delete(one.Id));
        Assert.Empty(service.List());
    }
}

/// <summary>The Backups screen's saved-game section: back up now, then restore or delete a kept copy.</summary>
public class SaveBackupsViewModelTests
{
    private sealed class FakeSaves : ISaveBackupsService
    {
        public List<SaveSnapshot> Items = new();
        public string? Next;
        public string? Restored;
        public string? Deleted;

        public bool IsAvailable => true;
        public string SaveFolder => @"C:\saves";
        public IReadOnlyList<SaveSnapshot> List() => Items;
        public string? BackUp()
        {
            if (Next is null) Items.Add(new SaveSnapshot("s1", DateTime.UtcNow, 1024, "p"));
            return Next;
        }
        public string? Restore(string id) { Restored = id; return Next; }
        public string? Delete(string id) { Deleted = id; if (Next is null) Items.RemoveAll(i => i.Id == id); return Next; }
    }

    [AvaloniaFact]
    public void Backing_up_the_save_asks_the_service_and_reports()
    {
        var saves = new FakeSaves();
        var vm = new BackupsViewModel(null, null, null, saves);

        vm.BackUpSaveCommand.Execute(null);

        Assert.Single(vm.Saves);
        Assert.Contains("backed up", vm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Restoring_a_save_copy_asks_the_service_and_reports()
    {
        var saves = new FakeSaves();
        saves.Items.Add(new SaveSnapshot("s1", DateTime.UtcNow, 1024, "p"));
        var vm = new BackupsViewModel(null, null, null, saves);

        vm.RestoreSaveCommand.Execute(vm.Saves[0]);

        Assert.Equal("s1", saves.Restored);
        Assert.Contains("restored", vm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void A_refused_save_backup_is_shown_plainly()
    {
        var saves = new FakeSaves { Next = "Close SCUM and try again." };
        var vm = new BackupsViewModel(null, null, null, saves);

        vm.BackUpSaveCommand.Execute(null);

        Assert.Contains("Close SCUM", vm.Message);
        Assert.Empty(vm.Saves);
    }
}
