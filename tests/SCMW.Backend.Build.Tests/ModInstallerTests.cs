using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

public class ModInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-inst-" + Guid.NewGuid().ToString("N"));
    private string Game(string sub) => Path.Combine(_root, sub);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private (ModInstaller installer, FileInstallStore store) NewInstaller()
    {
        var store = new FileInstallStore(Path.Combine(_root, "Builds"));
        return (new ModInstaller(store, Path.Combine(_root, "Backups")), store);
    }

    private string ModsOf(string game) => BuildNames.ModsFolder(game);

    [Fact]
    public void Install_copies_the_pak_into_mods_and_records_it()
    {
        var (installer, store) = NewInstaller();
        var game = Game("game");
        Directory.CreateDirectory(Path.Combine(game, "SCUM", "Content", "Paks"));
        var pak = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(pak, "pak");
        var record = new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", pak, "", DateTime.UtcNow, null);

        var result = installer.Install(game, record, pak);

        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(ModsOf(game), "SCMW_Tape_v1.0.0_P.pak")));
        Assert.NotNull(store.Load("p1"));
    }

    [Fact]
    public void A_switched_off_install_lands_as_a_disabled_file()
    {
        var (installer, store) = NewInstaller();
        var game = Game("game");
        Directory.CreateDirectory(Path.Combine(game, "SCUM", "Content", "Paks"));
        var pak = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(pak, "pak");
        var record = new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", pak, "", DateTime.UtcNow, null);

        var result = installer.Install(game, record, pak, enabled: false);

        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(ModsOf(game), "SCMW_Tape_v1.0.0_P.pak.disabled")));
        Assert.False(File.Exists(Path.Combine(ModsOf(game), "SCMW_Tape_v1.0.0_P.pak")));
        Assert.Equal(Path.Combine(ModsOf(game), "SCMW_Tape_v1.0.0_P.pak.disabled"),
                     store.Load("p1")!.InstalledPath);
    }

    [Fact]
    public void Rebuilding_switched_off_replaces_a_switched_on_copy()
    {
        var (installer, _) = NewInstaller();
        var game = Game("game");
        var mods = ModsOf(game); Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak"), "the copy that was switched on");
        var pak = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(pak, "new");
        var record = new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", pak, "", DateTime.UtcNow, null);

        var result = installer.Install(game, record, pak, enabled: false);

        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak.disabled")));
        Assert.False(File.Exists(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak")));  // the on copy is gone
    }

    [Fact]
    public void Installing_a_newer_version_removes_only_our_older_versions()
    {
        var (installer, _) = NewInstaller();
        var game = Game("game");
        var mods = ModsOf(game); Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak"), "old");
        File.WriteAllText(Path.Combine(mods, "SCMW_Tape-Extra_v9.9.9_P.pak"), "other project");
        File.WriteAllText(Path.Combine(mods, "SomeOtherMod.pak"), "someone else");
        var pak = Path.Combine(_root, "SCMW_Tape_v1.0.1_P.pak"); File.WriteAllText(pak, "new");
        var record = new InstallRecord("p1", "Tape", "1.0.1", "SCMW_Tape_v1.0.1_P.pak", pak, "", DateTime.UtcNow, null);

        var result = installer.Install(game, record, pak);

        Assert.True(result.Ok, result.Error);
        Assert.False(File.Exists(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak")));   // ours, replaced
        Assert.True(File.Exists(Path.Combine(mods, "SCMW_Tape-Extra_v9.9.9_P.pak"))); // prefix look-alike kept
        Assert.True(File.Exists(Path.Combine(mods, "SomeOtherMod.pak")));         // not ours, kept
    }

    [Fact]
    public void A_missing_scum_folder_fails_plainly_and_writes_nothing()
    {
        var (installer, _) = NewInstaller();
        Directory.CreateDirectory(_root);
        var game = Game("no-such-game");
        var pak = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(pak, "pak");
        var record = new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", pak, "", DateTime.UtcNow, null);

        var result = installer.Install(game, record, pak);

        Assert.False(result.Ok);
        Assert.False(Directory.Exists(ModsOf(game)));
    }

    [Fact]
    public void Remove_deletes_exactly_the_recorded_file_and_restores_a_backup()
    {
        var (installer, store) = NewInstaller();
        var game = Game("game");
        var mods = ModsOf(game); Directory.CreateDirectory(mods);
        var installed = Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak");
        File.WriteAllText(installed, "ours");
        var other = Path.Combine(mods, "SomeoneElses.pak"); File.WriteAllText(other, "not ours");
        var backup = Path.Combine(_root, "Backups", "clash.pak"); Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.WriteAllText(backup, "the file we replaced");
        store.Save(new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", "src", installed, DateTime.UtcNow, backup));

        var result = installer.Remove(game, "p1");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("the file we replaced", File.ReadAllText(installed));  // restored from backup
        Assert.True(File.Exists(other));                                    // never touched
        Assert.Null(store.Load("p1"));                                      // record cleared
    }

    [Fact]
    public void Remove_with_no_record_does_nothing_and_succeeds()
    {
        var (installer, _) = NewInstaller();
        var result = installer.Remove(Game("game"), "never-installed");
        Assert.True(result.Ok);
    }

    [Fact]
    public void Reinstalling_our_own_pak_makes_no_backup_so_removing_really_removes_it()
    {
        var (installer, store) = NewInstaller();
        var game = Game("game");
        Directory.CreateDirectory(Path.Combine(game, "SCUM", "Content", "Paks"));
        Directory.CreateDirectory(_root);

        var first = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(first, "one");
        Assert.True(installer.Install(game,
            new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", first, "", DateTime.UtcNow, null), first).Ok);

        // Rebuild + reinstall the same version: the file already there is OUR OWN pak, not a
        // foreign one, so nothing is backed up.
        var second = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(second, "two");
        Assert.True(installer.Install(game,
            new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", second, "", DateTime.UtcNow, null), second).Ok);
        Assert.Null(store.Load("p1")!.BackupPath);

        var modsPak = Path.Combine(ModsOf(game), "SCMW_Tape_v1.0.0_P.pak");
        Assert.True(installer.Remove(game, "p1").Ok);

        // The mod is really gone — it is not resurrected from a backup of itself.
        Assert.False(File.Exists(modsPak));
        Assert.Null(store.Load("p1"));
    }

    [Fact]
    public void Installing_again_replaces_a_switched_off_same_name_twin()
    {
        var (installer, _) = NewInstaller();
        var game = Game("game");
        var mods = ModsOf(game); Directory.CreateDirectory(mods);
        Directory.CreateDirectory(_root);
        var pak = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(pak, "new");
        var record = new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", pak, "", DateTime.UtcNow, null);

        // A previous install was switched off on the Mods screen (Plan 14 renames the file).
        File.WriteAllText(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak.disabled"), "old-disabled");

        Assert.True(installer.Install(game, record, pak).Ok);

        Assert.True(File.Exists(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak")));
        Assert.False(File.Exists(Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak.disabled")));   // no twin left
    }

    [Fact]
    public void A_locked_source_fails_the_install_and_leaves_the_older_version_in_place()
    {
        var (installer, _) = NewInstaller();
        var game = Game("game");
        var mods = ModsOf(game); Directory.CreateDirectory(mods);
        var older = Path.Combine(mods, "SCMW_Tape_v1.0.0_P.pak");
        File.WriteAllText(older, "old");
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "SCMW_Tape_v1.0.1_P.pak"); File.WriteAllText(source, "new");
        var record = new InstallRecord("p1", "Tape", "1.0.1", "SCMW_Tape_v1.0.1_P.pak", source, "", DateTime.UtcNow, null);

        InstallOutcome result;
        using (var hold = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = installer.Install(game, record, source);
        }

        Assert.False(result.Ok);
        Assert.True(File.Exists(older));   // nothing was removed
    }

    [Fact]
    public void A_busy_file_failure_names_the_game_holding_it()
    {
        var (installer, _) = NewInstaller();
        var game = Game("game");
        Directory.CreateDirectory(Path.Combine(game, "SCUM", "Content", "Paks"));
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "SCMW_Tape_v1.0.1_P.pak"); File.WriteAllText(source, "new");
        var record = new InstallRecord("p1", "Tape", "1.0.1", "SCMW_Tape_v1.0.1_P.pak", source, "", DateTime.UtcNow, null);

        InstallOutcome result;
        using (var hold = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = installer.Install(game, record, source);
        }

        Assert.False(result.Ok);
        Assert.Equal(WriteFailureKind.GameRunning, result.Failure);
        Assert.Contains("close", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reinstalling_after_a_rename_removes_the_old_named_pak()
    {
        var (installer, _) = NewInstaller();
        var game = Game("game");
        Directory.CreateDirectory(Path.Combine(game, "SCUM", "Content", "Paks"));
        Directory.CreateDirectory(_root);

        var first = Path.Combine(_root, "SCMW_Tape_v1.0.0_P.pak"); File.WriteAllText(first, "one");
        installer.Install(game, new InstallRecord("p1", "Tape", "1.0.0", "SCMW_Tape_v1.0.0_P.pak", first, "", DateTime.UtcNow, null), first);
        var old = Path.Combine(ModsOf(game), "SCMW_Tape_v1.0.0_P.pak");
        Assert.True(File.Exists(old));

        var second = Path.Combine(_root, "SCMW_Tape-Renamed_v1.0.0_P.pak"); File.WriteAllText(second, "two");
        installer.Install(game, new InstallRecord("p1", "Tape Renamed", "1.0.0", "SCMW_Tape-Renamed_v1.0.0_P.pak", second, "", DateTime.UtcNow, null), second);

        Assert.False(File.Exists(old));   // the old-named pak is gone
        Assert.True(File.Exists(Path.Combine(ModsOf(game), "SCMW_Tape-Renamed_v1.0.0_P.pak")));
    }
}
