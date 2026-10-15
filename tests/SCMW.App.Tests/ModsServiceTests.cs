using SCMW.App;
using SCMW.Backend.Build;

namespace SCMW.App.Tests;

public class ModsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-mods-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string GameRoot => Path.Combine(_root, "game");
    private string Workspace => Path.Combine(_root, "ws");
    private string Mods => Path.Combine(GameRoot, "SCUM", "Content", "Paks", "~mods");

    private ModsService NewService()
    {
        // The Paks folder must exist for the service to consider the game configured.
        Directory.CreateDirectory(Path.Combine(GameRoot, "SCUM", "Content", "Paks"));
        return new ModsService(GameRoot, Workspace);
    }

    private void Put(string fileName, string content = "pak")
    {
        Directory.CreateDirectory(Mods);
        File.WriteAllText(Path.Combine(Mods, fileName), content);
    }

    /// <summary>Record one of our built mods, as Plan 13's installer would.</summary>
    private void RecordOurs(string projectId, string fileName)
    {
        var dir = Path.Combine(Workspace, "Builds", projectId);
        Directory.CreateDirectory(dir);
        var record = new InstallRecord(projectId, "Proj", "1.0.0", fileName,
            Path.Combine(_root, fileName), Path.Combine(Mods, fileName), DateTime.UtcNow, null);
        File.WriteAllText(Path.Combine(dir, "installed.json"),
            System.Text.Json.JsonSerializer.Serialize(record));
    }

    [Fact]
    public void Lists_on_and_off_mods_with_the_right_badges()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak");
        Put("SomeExternal.pak.disabled");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");

        var mods = svc.List();

        var mine = mods.Single(m => m.DisplayName == "SCMW_My-mod_v1.0.0_P");
        Assert.True(mine.IsOn);
        Assert.False(mine.IsExternal);                       // found in our records

        var other = mods.Single(m => m.DisplayName == "SomeExternal");
        Assert.False(other.IsOn);                            // .pak.disabled
        Assert.True(other.IsExternal);
    }

    /// <summary>The plain-text note carried inside a pack.</summary>
    private static string ReadNote(System.IO.Compression.ZipArchive zip)
    {
        var entry = zip.GetEntry(ModsService.NoteFileName)!;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Packing_puts_only_the_mods_SCMW_made_in_the_zip()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak");
        Put("SomeExternal.pak");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");
        var zipPath = Path.Combine(_root, "pack.zip");

        var outcome = svc.PackOurMods(zipPath);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(1, outcome.Packed);

        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("SCMW_My-mod_v1.0.0_P.pak", names);
        Assert.DoesNotContain("SomeExternal.pak", names);
        Assert.Contains(ModsService.NoteFileName, names);

        // The note names what was left out, and why.
        Assert.Contains("SomeExternal.pak — added from a file", ReadNote(zip));
    }

    [Fact]
    public void The_read_me_introduces_the_app_and_links_to_it()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");
        var zipPath = Path.Combine(_root, "pack.zip");

        Assert.True(svc.PackOurMods(zipPath).Ok);

        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var note = ReadNote(zip);

        Assert.Contains($"What {AppInfo.Name} can do for you", note);
        Assert.Contains(AppInfo.About, note);
        Assert.Contains(AppInfo.NexusUrl, note);
        Assert.Contains(AppInfo.RepositoryUrl, note);
        Assert.Contains(AppInfo.Version, note);

        // It says a mod manager will do too, and that this app is one.
        Assert.Contains("or use your preferred mod manager", note);
        Assert.Contains("is a mod manager as well as a mod maker", note);
        Assert.Contains("Drag a .pak into it", note);

        // The switched-off note, and the creator's recommendation.
        Assert.Contains("a mod name ending in \".disabled\"", note);
        Assert.Contains("recommend downloading it and using it", note);
    }

    [Fact]
    public void Packing_a_switched_off_mod_keeps_it_switched_off()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak.disabled");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");
        var zipPath = Path.Combine(_root, "pack.zip");

        var outcome = svc.PackOurMods(zipPath);

        Assert.True(outcome.Ok, outcome.Error);
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        Assert.Contains("SCMW_My-mod_v1.0.0_P.pak.disabled", zip.Entries.Select(e => e.FullName));

        // The name itself carries the state, and the note explains what ".disabled" means.
        Assert.Contains("a mod name ending in \".disabled\"", ReadNote(zip));
    }

    [Fact]
    public void Packing_leaves_out_a_mod_another_tool_keeps_and_says_why()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");
        var other = Path.Combine(GameRoot, "SCUM", "Content", "Paks", "LogicMods");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "SomeLogicMod.pak"), "pak");

        var outcome = svc.PackOurMods(Path.Combine(_root, "pack.zip"));

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Single(outcome.LeftOut);
        Assert.Contains("another tool", outcome.LeftOut[0]);
        Assert.Contains("LogicMods", outcome.LeftOut[0]);
    }

    [Fact]
    public void Packing_with_nothing_of_ours_to_pack_says_so_plainly()
    {
        var svc = NewService();
        Put("SomeExternal.pak");

        var outcome = svc.PackOurMods(Path.Combine(_root, "pack.zip"));

        Assert.False(outcome.Ok);
        Assert.Contains("no SCMW mods", outcome.Error ?? "");
    }

    [Fact]
    public void A_disabled_mod_we_built_keeps_its_SCMW_badge()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak.disabled");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");

        Assert.False(Assert.Single(svc.List()).IsExternal);
    }

    [Fact]
    public void A_missing_mods_folder_lists_nothing()
    {
        var svc = new ModsService(GameRoot, Workspace);      // Paks folder not created
        Assert.Empty(svc.List());
        Assert.False(svc.IsAvailable);
    }

    private string OtherMods(string folder) =>
        Path.Combine(GameRoot, "SCUM", "Content", "Paks", folder);

    [Fact] // design §15: recognise other tools' folders, but never touch them
    public void A_mod_in_another_tools_folder_is_listed_but_not_managed()
    {
        var svc = NewService();
        var dir = OtherMods("Mods");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "VortexMod.pak"), "pak");

        var entry = Assert.Single(svc.List());
        Assert.Equal("VortexMod", entry.DisplayName);
        Assert.False(entry.Managed);
        Assert.Equal("Mods", entry.Location);
        Assert.True(entry.IsExternal);
    }

    [Fact]
    public void Another_tools_mod_is_never_switched_or_removed()
    {
        var svc = NewService();
        var dir = OtherMods("LogicMods");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "Logic.pak");
        File.WriteAllText(file, "pak");

        var switched = svc.SetEnabled("Logic.pak", false);
        Assert.False(switched.Ok);
        Assert.Contains("leaves it alone", switched.Error);
        Assert.True(File.Exists(file));                       // untouched

        var removed = svc.Remove("Logic.pak");
        Assert.False(removed.Ok);
        Assert.True(File.Exists(file));                       // untouched
    }

    [Fact]
    public void Switching_a_mod_off_renames_it_and_back_on_restores_it()
    {
        var svc = NewService();
        Put("SomeExternal.pak");

        Assert.True(svc.SetEnabled("SomeExternal.pak", enabled: false).Ok);
        Assert.False(File.Exists(Path.Combine(Mods, "SomeExternal.pak")));
        Assert.True(File.Exists(Path.Combine(Mods, "SomeExternal.pak.disabled")));
        Assert.False(Assert.Single(svc.List()).IsOn);

        Assert.True(svc.SetEnabled("SomeExternal.pak.disabled", enabled: true).Ok);
        Assert.True(File.Exists(Path.Combine(Mods, "SomeExternal.pak")));
        Assert.True(Assert.Single(svc.List()).IsOn);
    }

    [Fact]
    public void Switching_all_turns_every_mod_on_or_off()
    {
        var svc = NewService();
        Put("A.pak");
        Put("B.pak.disabled");
        Put("C.pak");

        Assert.True(svc.SetAllEnabled(enabled: true).Ok);
        Assert.All(svc.List(), m => Assert.True(m.IsOn));

        Assert.True(svc.SetAllEnabled(enabled: false).Ok);
        Assert.All(svc.List(), m => Assert.False(m.IsOn));
    }

    [Fact]
    public void Switching_a_locked_file_fails_plainly_and_changes_nothing()
    {
        var svc = NewService();
        Put("A.pak");
        var locked = Path.Combine(Mods, "A.pak");
        ModsResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = svc.SetEnabled("A.pak", enabled: false);
        }

        Assert.False(result.Ok);
        Assert.True(File.Exists(locked));                    // unchanged
        Assert.False(File.Exists(locked + ".disabled"));
        Assert.Equal(WriteFailureKind.GameRunning, result.Failure);
        Assert.Contains("close", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Switching_all_reports_a_failure_rather_than_silently_stopping_half_way()
    {
        var svc = NewService();
        Put("A.pak");
        Put("B.pak");
        var locked = Path.Combine(Mods, "B.pak");
        ModsResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = svc.SetAllEnabled(enabled: false);
        }

        Assert.False(result.Ok);                             // the caller (the launch button) is told
        Assert.True(File.Exists(locked));                    // the locked file is untouched
    }

    [Fact]
    public void Removing_a_mod_backs_it_up_first_then_deletes_it()
    {
        var svc = NewService();
        Put("SomeExternal.pak", "the mod bytes");

        Assert.True(svc.Remove("SomeExternal.pak").Ok);

        Assert.False(File.Exists(Path.Combine(Mods, "SomeExternal.pak")));
        var backups = Directory.GetFiles(Path.Combine(Workspace, "Backups", "mods"));
        Assert.Single(backups);
        Assert.Equal("the mod bytes", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Removing_a_locked_mod_fails_plainly_and_keeps_it()
    {
        var svc = NewService();
        Put("SomeExternal.pak");
        var locked = Path.Combine(Mods, "SomeExternal.pak");
        ModsResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = svc.Remove("SomeExternal.pak");
        }

        Assert.False(result.Ok);
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void Importing_a_pak_copies_it_in_switched_on_and_records_it()
    {
        var svc = NewService();
        var source = Path.Combine(_root, "Downloaded.pak"); File.WriteAllText(source, "downloaded");

        var outcome = svc.Import(new[] { source });

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(1, outcome.Imported);
        Assert.True(File.Exists(Path.Combine(Mods, "Downloaded.pak")));
        Assert.True(File.Exists(source));                      // the original is left alone
        var entry = svc.List().Single(m => m.DisplayName == "Downloaded");
        Assert.True(entry.IsOn);
        Assert.True(entry.IsExternal);
    }

    [Fact]
    public void Importing_a_name_already_installed_is_refused_and_overwrites_nothing()
    {
        var svc = NewService();
        Put("Downloaded.pak", "the installed one");
        var source = Path.Combine(_root, "Downloaded.pak"); File.WriteAllText(source, "the new one");

        var outcome = svc.Import(new[] { source });

        Assert.Equal(0, outcome.Imported);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(0, outcome.SkippedNotPak);        // skipped as a duplicate, not a bad file
        Assert.False(outcome.Ok);
        Assert.Equal("the installed one", File.ReadAllText(Path.Combine(Mods, "Downloaded.pak")));
    }

    [Fact]
    public void Importing_a_non_pak_is_skipped()
    {
        var svc = NewService();
        var source = Path.Combine(_root, "notes.txt"); File.WriteAllText(source, "hi");
        var outcome = svc.Import(new[] { source });
        Assert.Equal(0, outcome.Imported);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(1, outcome.SkippedNotPak);        // skipped because it is not a .pak
    }

    [Fact]
    public void Removing_a_mod_we_built_clears_its_install_record()
    {
        var svc = NewService();
        Put("SCMW_My-mod_v1.0.0_P.pak");
        RecordOurs("p1", "SCMW_My-mod_v1.0.0_P.pak");

        Assert.True(svc.Remove("SCMW_My-mod_v1.0.0_P.pak").Ok);

        // The Build screen reads this record; a stale one would resurrect the file.
        Assert.False(File.Exists(Path.Combine(Workspace, "Builds", "p1", "installed.json")));
    }

    [Fact]
    public void Importing_a_locked_source_reports_the_game_is_busy()
    {
        var svc = NewService();
        var source = Path.Combine(_root, "Downloaded.pak"); File.WriteAllText(source, "downloaded");

        ImportOutcome outcome;
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = svc.Import(new[] { source });
        }

        Assert.False(outcome.Ok);
        Assert.Equal(WriteFailureKind.GameRunning, outcome.Failure);
        Assert.Contains("close", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Importing_leaves_no_temporary_files_behind()
    {
        var svc = NewService();
        var source = Path.Combine(_root, "Downloaded.pak"); File.WriteAllText(source, "downloaded");

        svc.Import(new[] { source });

        Assert.Empty(Directory.GetFiles(Mods, "*.adding"));
    }
}
