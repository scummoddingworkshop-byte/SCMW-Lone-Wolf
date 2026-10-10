using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class BackupsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-bak-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;

    public BackupsServiceTests()
    {
        Directory.CreateDirectory(_root);
        _game = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(_game, "SCUM", "Content", "Paks"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Mods => Path.Combine(_game, "SCUM", "Content", "Paks", "~mods");
    private string ModsBackups => Path.Combine(_root, "Backups", "mods");
    private string ProjectsBackups => Path.Combine(_root, "Backups", "deleted-projects");
    private string Projects => Path.Combine(_root, "Projects");

    private BackupsService New() => new(_game, _root);

    [AvaloniaFact]
    public void Lists_removed_mods_and_deleted_projects()
    {
        Directory.CreateDirectory(ModsBackups);
        File.WriteAllText(Path.Combine(ModsBackups, "SCMW_Tape_v1.0.0_P (20261008-101112).pak"), "x");
        Directory.CreateDirectory(Path.Combine(ProjectsBackups, "My first mod abc12345 (20261007-090000)"));

        var entries = New().List();

        Assert.Equal(2, entries.Count);
        Assert.Equal("SCMW_Tape_v1.0.0_P.pak", entries.Single(e => e.Kind == BackupKind.Mod).DisplayName);
        Assert.Equal("My first mod abc12345", entries.Single(e => e.Kind == BackupKind.Project).DisplayName);
    }

    [AvaloniaFact]
    public void Restoring_a_mod_copies_it_back_under_its_original_name()
    {
        Directory.CreateDirectory(ModsBackups);
        File.WriteAllText(Path.Combine(ModsBackups, "SCMW_Tape_v1.0.0_P (20261008-101112).pak"), "pak");

        var entry = New().List().Single(e => e.Kind == BackupKind.Mod);
        var result = New().Restore(entry);

        Assert.True(result.Ok);
        Assert.True(File.Exists(Path.Combine(Mods, "SCMW_Tape_v1.0.0_P.pak")));
    }

    [AvaloniaFact]
    public void Restoring_a_mod_that_is_already_there_is_refused()
    {
        Directory.CreateDirectory(Mods);
        File.WriteAllText(Path.Combine(Mods, "SCMW_Tape_v1.0.0_P.pak"), "already");
        Directory.CreateDirectory(ModsBackups);
        File.WriteAllText(Path.Combine(ModsBackups, "SCMW_Tape_v1.0.0_P (20261008-101112).pak"), "pak");

        var entry = New().List().Single(e => e.Kind == BackupKind.Mod);
        var result = New().Restore(entry);

        Assert.False(result.Ok);
        Assert.Contains("already", result.Error ?? "");
    }

    [AvaloniaFact]
    public void Restoring_a_project_moves_its_folder_back()
    {
        Directory.CreateDirectory(Path.Combine(ProjectsBackups, "My mod abc (20261007-090000)"));

        var entry = New().List().Single(e => e.Kind == BackupKind.Project);
        var result = New().Restore(entry);

        Assert.True(result.Ok);
        Assert.True(Directory.Exists(Path.Combine(Projects, "My mod abc")));
        Assert.False(Directory.Exists(Path.Combine(ProjectsBackups, "My mod abc (20261007-090000)")));
    }

    [AvaloniaFact]
    public void Deleting_a_backup_removes_it_for_good()
    {
        Directory.CreateDirectory(ModsBackups);
        var file = Path.Combine(ModsBackups, "X (20261008-101112).pak");
        File.WriteAllText(file, "x");

        var entry = New().List().Single();
        Assert.True(New().Delete(entry).Ok);
        Assert.False(File.Exists(file));
    }
}

public class BackupsViewModelTests
{
    private sealed class FakeBackups : IBackupsService
    {
        public List<BackupEntry> Entries = new();
        public ModsResult Next = new(true, null);
        public string? Restored;
        public string? Deleted;

        public bool IsAvailable => true;
        public string Folder => "";
        public IReadOnlyList<BackupEntry> List() => Entries;
        public ModsResult Restore(BackupEntry entry) { Restored = entry.Path; if (Next.Ok) Entries.Remove(entry); return Next; }
        public ModsResult Delete(BackupEntry entry) { Deleted = entry.Path; if (Next.Ok) Entries.Remove(entry); return Next; }
    }

    [AvaloniaFact]
    public void An_empty_backups_folder_shows_the_empty_state()
    {
        var vm = new BackupsViewModel(new FakeBackups());

        Assert.True(vm.ShowEmptyState);
        Assert.Equal("0 backups kept", vm.Summary);
    }

    [AvaloniaFact]
    public void Restoring_a_backup_asks_the_service_and_reports()
    {
        var fake = new FakeBackups();
        fake.Entries.Add(new BackupEntry(BackupKind.Mod, "x.pak", DateTime.UtcNow, "p"));
        var vm = new BackupsViewModel(fake);

        vm.RestoreCommand.Execute(vm.Backups[0]);

        Assert.Equal("p", fake.Restored);
        Assert.Contains("restored", vm.Message);
        Assert.True(vm.ShowEmptyState);        // the list refreshed away
    }

    [AvaloniaFact]
    public void A_refused_restore_is_shown_plainly()
    {
        var fake = new FakeBackups { Next = new ModsResult(false, "A mod with that name is already there.") };
        fake.Entries.Add(new BackupEntry(BackupKind.Mod, "x.pak", DateTime.UtcNow, "p"));
        var vm = new BackupsViewModel(fake);

        vm.RestoreCommand.Execute(vm.Backups[0]);

        Assert.Contains("already", vm.Message);
        Assert.True(vm.HasBackups);            // nothing removed
    }

    [AvaloniaFact]
    public void Deleting_asks_first_then_removes()
    {
        var fake = new FakeBackups();
        fake.Entries.Add(new BackupEntry(BackupKind.Project, "My mod", DateTime.UtcNow, "p"));
        var vm = new BackupsViewModel(fake);

        var row = vm.Backups[0];
        vm.RequestDeleteCommand.Execute(row);
        Assert.True(row.IsConfirmingDelete);

        vm.ConfirmDeleteCommand.Execute(row);

        Assert.Equal("p", fake.Deleted);
        Assert.True(vm.ShowEmptyState);
    }
}
