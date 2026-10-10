using Xunit; using SCMW.Core.Projects; using SCMW.Core.Workspace;

public class ProjectServiceTests
{
    private static WorkspacePaths TempWs()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-proj-" + Guid.NewGuid());
        return WorkspaceService.Ensure(root);
    }

    [Fact]
    public void Create_writes_project_file_and_list_returns_it()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Military Backpacks", "Dist tweaks").Project!;
        Assert.True(File.Exists(Path.Combine(p.FolderPath, "project.json")));
        Assert.Single(svc.List().Where(x => x.Id == p.Id));
    }

    [Fact] // Review Focus #4 (Plan 2d): duplicate names are refused, not silently distinct
    public void Duplicate_name_is_refused_and_creates_nothing()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var a = svc.Create("Custom Items").Project;
        Assert.NotNull(a);

        var before = Directory.GetDirectories(ws.Projects).Length;

        var again = svc.Create("Custom Items");

        Assert.Null(again.Project);
        Assert.Equal(ProjectService.DuplicateNameMessage, again.ErrorMessage);
        Assert.Null(again.IconWarning);
        Assert.Equal(before, Directory.GetDirectories(ws.Projects).Length);
        Assert.Single(svc.List());
    }

    // Extra (brief decision 2): folder names must be filesystem-safe while the display
    // Name keeps whatever the user typed.
    [Fact]
    public void Create_sanitizes_illegal_filename_chars_but_keeps_display_name()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        const string name = "Bad/Name:With*Illegal?Chars";

        var p = svc.Create(name).Project!;

        Assert.Equal(name, p.Name);                                   // display name intact
        var folderName = Path.GetFileName(p.FolderPath);
        foreach (var c in Path.GetInvalidFileNameChars())
            Assert.True(folderName.IndexOf(c) < 0, $"folder name still contains '{c}'");
        Assert.StartsWith(ws.Projects, p.FolderPath);
        Assert.True(File.Exists(Path.Combine(p.FolderPath, "project.json")));
    }

    // Extra (brief decision 2): List is ordered by ModifiedUtc descending, reading files
    // written by another process/writer (not just ones this service just created).
    [Fact]
    public void List_sorts_by_modified_utc_descending()
    {
        var ws = TempWs();
        SeedProject(ws, "aaaaaaaa", "Oldest", new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedProject(ws, "bbbbbbbb", "Newest", new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedProject(ws, "cccccccc", "Middle", new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var svc = new ProjectService(ws);

        Assert.Equal(new[] { "Newest", "Middle", "Oldest" }, svc.List().Select(x => x.Name).ToArray());
    }

    // Extra: project.json is camelCase and GetById finds the same record List returns.
    [Fact]
    public void Project_file_is_camel_case_and_get_by_id_round_trips()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Round Trip", "desc").Project!;

        var json = File.ReadAllText(Path.Combine(p.FolderPath, "project.json"));
        Assert.Contains("\"modifiedUtc\"", json);
        Assert.DoesNotContain("\"ModifiedUtc\"", json);

        var found = svc.GetById(p.Id);
        Assert.NotNull(found);
        Assert.Equal(p.Name, found!.Name);
        Assert.Equal("desc", found.Description);
    }

    private static void SeedProject(WorkspacePaths ws, string id, string name, DateTime modifiedUtc)
    {
        var folder = Path.Combine(ws.Projects, $"{name} {id}");
        Directory.CreateDirectory(folder);
        var json =
            "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"description\":\"\"," +
            "\"createdUtc\":\"" + modifiedUtc.ToString("o") + "\"," +
            "\"modifiedUtc\":\"" + modifiedUtc.ToString("o") + "\"}";
        File.WriteAllText(Path.Combine(folder, "project.json"), json);
    }

    // Plan 2c Task D: an optional icon is copied into the project folder (never referenced in
    // place), the copied name keeps the original extension lowercased, and no icon keeps the
    // legacy script working unchanged.

    [Theory]
    [InlineData(".PNG", "icon.png")]
    [InlineData(".Jpg", "icon.jpg")]
    [InlineData(".JPEG", "icon.jpeg")]
    public void Create_with_icon_copies_file_and_records_lowercased_icon_name(string extension, string expected)
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var src = WriteTempFile(extension, 64);

        var result = svc.Create("With Icon", "desc", src);
        var p = result.Project!;

        Assert.Null(result.IconWarning);                                    // a usable icon warns about nothing
        Assert.Equal("With Icon", p.Name);                                  // display name unchanged
        Assert.Equal(expected, p.IconFileName);
        var copied = Path.Combine(p.FolderPath, expected);
        Assert.Equal(copied, p.IconPath);
        Assert.True(File.Exists(copied));                                   // copied into the folder
        Assert.True(File.Exists(src));                                      // original untouched
        Assert.Equal(new FileInfo(src).Length, new FileInfo(copied).Length);

        var loaded = Assert.Single(svc.List().Where(x => x.Id == p.Id));
        Assert.Equal(expected, loaded.IconFileName);
        Assert.Equal(copied, loaded.IconPath);
    }

    [Fact]
    public void Create_without_icon_leaves_icon_null_and_writes_no_icon_field()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);

        var result = svc.Create("Plain Project", "no icon here");
        var p = result.Project!;

        Assert.Null(result.IconWarning);
        Assert.Null(p.IconFileName);
        Assert.Null(p.IconPath);
        var json = File.ReadAllText(Path.Combine(p.FolderPath, ProjectService.ProjectFileName));
        Assert.DoesNotContain("iconFileName", json);                        // legacy file shape kept
        Assert.Null(Assert.Single(svc.List()).IconFileName);
    }

    [Fact] // Review Focus #2: an unusable icon must not block creation
    public void Create_with_unsupported_extension_still_creates_without_icon_and_warns()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var src = WriteTempFile(".txt", 64);

        var result = svc.Create("Bad Icon", "kept description", src);

        Assert.Equal(ProjectService.IconWarningMessage, result.IconWarning);
        Assert.Null(result.Project!.IconFileName);
        Assert.Null(result.Project!.IconPath);
        Assert.Equal("Bad Icon", result.Project!.Name);                      // typed input never lost
        Assert.Equal("kept description", result.Project!.Description);
        Assert.Single(svc.List());
        Assert.False(File.Exists(Path.Combine(result.Project!.FolderPath, "icon.txt")));
    }

    [Fact] // Finding 2: contents must match the extension, not just the name
    public void Create_with_text_file_renamed_png_creates_without_icon_and_warns()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var src = Path.Combine(Path.GetTempPath(),
            "scmw-notimage-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(src, "this is plainly not a PNG, whatever the name says");

        var result = svc.Create("Fake Icon", "kept", src);

        Assert.Equal(ProjectService.IconWarningMessage, result.IconWarning);
        Assert.Null(result.Project!.IconFileName);
        Assert.Null(result.Project!.IconPath);
        Assert.Equal("Fake Icon", result.Project!.Name);          // typed input never lost
        Assert.Equal("kept", result.Project!.Description);
        Assert.False(File.Exists(Path.Combine(result.Project!.FolderPath, "icon.png")));
        Assert.Single(svc.List());
    }

    [Fact] // Finding 2: an unreadable/locked file must not throw out of Create.
    // A Windows exclusive lock can't be turned into UnauthorizedAccessException on demand here,
    // so the exclusive-handle case stands in for "any file-access failure" and exercises the
    // same catch path (the signature read hits a sharing violation → IOException).
    public void Create_with_locked_icon_creates_without_icon_and_warns()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var src = WriteTempFile(".png", 64);                    // valid signature on disk
        using var hold = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = svc.Create("Locked Icon", "d", src);

        Assert.Equal(ProjectService.IconWarningMessage, result.IconWarning);
        Assert.Null(result.Project!.IconFileName);
        Assert.True(File.Exists(Path.Combine(result.Project!.FolderPath, ProjectService.ProjectFileName)));
        Assert.Single(svc.List());
    }

    [Fact]
    public void Create_with_missing_icon_still_creates_without_icon_and_warns()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var missing = Path.Combine(Path.GetTempPath(), "scmw-missing-" + Guid.NewGuid().ToString("N") + ".png");

        var result = svc.Create("Bad Icon", "d", missing);

        Assert.Equal(ProjectService.IconWarningMessage, result.IconWarning);
        Assert.Null(result.Project!.IconFileName);
        Assert.True(File.Exists(Path.Combine(result.Project!.FolderPath, ProjectService.ProjectFileName)));
        Assert.Single(svc.List());
    }

    [Fact]
    public void Create_with_oversized_icon_still_creates_without_icon_and_warns()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var src = WriteTempFile(".jpg", ProjectService.MaxIconBytes + 1);

        var result = svc.Create("Bad Icon", "d", src);

        Assert.Equal(ProjectService.IconWarningMessage, result.IconWarning);
        Assert.Null(result.Project!.IconFileName);
        Assert.False(File.Exists(Path.Combine(result.Project!.FolderPath, "icon.jpg")));
        Assert.Single(svc.List());
    }

    [Fact] // hard requirement: existing project.json files (no icon field) must keep loading
    public void Legacy_project_json_without_icon_loads_with_null_icon()
    {
        var ws = TempWs();
        SeedProject(ws, "legacy01", "Legacy", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var p = Assert.Single(new ProjectService(ws).List());

        Assert.Equal("Legacy", p.Name);
        Assert.Null(p.IconFileName);
        Assert.Null(p.IconPath);
    }

    private static string WriteTempFile(string extension, long bytes)
    {
        var path = Path.Combine(Path.GetTempPath(),
            "scmw-icon-" + Guid.NewGuid().ToString("N") + extension);
        using var fs = File.Create(path);
        // The contents must carry the real signature for the extension, or Core rejects them
        // (Finding 2). PNG magic for .png; JPEG SOI for .jpg/.jpeg.
        fs.Write(extension.ToLowerInvariant() == ".png"
            ? new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            : new byte[] { 0xFF, 0xD8, 0xFF });
        if (bytes > fs.Length)
            fs.SetLength(bytes);
        return path;
    }

    // ---- Plan 2d Task 1: unique names, rename, inline updates, recoverable delete ----

    [Theory]
    [InlineData("Custom Items")]        // exact duplicate
    [InlineData("CUSTOM ITEMS")]        // case-insensitive
    [InlineData("  Custom   Items  ")]  // whitespace-insensitive (trim + collapse)
    public void Create_refuses_duplicate_names_and_writes_nothing(string duplicate)
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        svc.Create("Custom Items");

        var before = Directory.GetDirectories(ws.Projects).Length;
        var result = svc.Create(duplicate);

        Assert.Null(result.Project);
        Assert.Equal(ProjectService.DuplicateNameMessage, result.ErrorMessage);
        Assert.Equal(before, Directory.GetDirectories(ws.Projects).Length);
        Assert.Single(svc.List());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_refuses_blank_names_and_writes_nothing(string blank)
    {
        var ws = TempWs(); var svc = new ProjectService(ws);

        var result = svc.Create(blank);

        Assert.Null(result.Project);
        Assert.Equal(ProjectService.InvalidNameMessage, result.ErrorMessage);
        Assert.Empty(svc.List());
        Assert.Empty(Directory.GetDirectories(ws.Projects));
    }

    [Theory]
    [InlineData("My  Mod", "My Mod")]
    [InlineData("  My Mod  ", "My Mod")]
    [InlineData("My\tMod", "My Mod")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeName_trims_and_collapses_whitespace(string input, string expected)
    {
        Assert.Equal(expected, ProjectService.NormalizeName(input));
    }

    [Fact]
    public void Rename_moves_folder_keeps_id_updates_json_and_keeps_icon()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Old Name", "d", WriteTempFile(".png", 64)).Project!;
        var oldFolder = p.FolderPath;

        var error = svc.Rename(p.Id, "New  Name");

        Assert.Null(error);
        var renamed = svc.GetById(p.Id);
        Assert.NotNull(renamed);
        Assert.Equal("New  Name", renamed!.Name);          // trimmed, inner spacing kept
        Assert.Equal(p.Id, renamed.Id);                    // id kept
        Assert.False(Directory.Exists(oldFolder));         // old folder gone
        Assert.True(Directory.Exists(renamed.FolderPath));
        Assert.EndsWith($"New  Name {p.Id}", Path.GetFileName(renamed.FolderPath));
        Assert.Equal("icon.png", renamed.IconFileName);
        Assert.True(File.Exists(renamed.IconPath!));       // icon still resolves after the move
        var json = File.ReadAllText(Path.Combine(renamed.FolderPath, ProjectService.ProjectFileName));
        Assert.Contains("\"name\": \"New  Name\"", json);
    }

    [Fact]
    public void Rename_to_duplicate_is_refused_and_nothing_changes()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        svc.Create("Alpha");
        var b = svc.Create("Beta", "keep me").Project!;
        var bFolder = b.FolderPath;

        var error = svc.Rename(b.Id, "  alpha  ");

        Assert.Equal(ProjectService.DuplicateNameMessage, error);
        var still = svc.GetById(b.Id);
        Assert.NotNull(still);
        Assert.Equal("Beta", still!.Name);
        Assert.Equal(bFolder, still.FolderPath);
        Assert.True(Directory.Exists(bFolder));
    }

    [Fact]
    public void Rename_to_invalid_is_refused_and_nothing_changes()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Alpha").Project!;
        var folder = p.FolderPath;

        var error = svc.Rename(p.Id, "   ");

        Assert.Equal(ProjectService.InvalidNameMessage, error);
        Assert.Equal("Alpha", svc.GetById(p.Id)!.Name);
        Assert.True(Directory.Exists(folder));
    }

    [Fact] // Review Focus 3 failure path: a blocked folder move leaves every stored value unchanged.
    public void Rename_failure_leaves_name_folder_and_json_unchanged()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Original Name", "keep").Project!;
        var folder = p.FolderPath;
        var jsonBefore = File.ReadAllText(Path.Combine(folder, ProjectService.ProjectFileName));

        // Occupy the intended destination so the folder move cannot succeed.
        var blocker = Path.Combine(ws.Projects, $"Blocked Name {p.Id}");
        File.WriteAllText(blocker, "occupied");

        var error = svc.Rename(p.Id, "Blocked Name");

        Assert.Equal(ProjectService.RenameFailedMessage, error);
        var after = svc.GetById(p.Id)!;
        Assert.Equal("Original Name", after.Name);
        Assert.Equal(folder, after.FolderPath);
        Assert.True(Directory.Exists(folder));
        Assert.Equal(jsonBefore,
            File.ReadAllText(Path.Combine(folder, ProjectService.ProjectFileName)));
        Assert.True(File.Exists(blocker));   // the blocker is untouched
    }

    [Fact] // Ruling 3: the display name is stored trimmed, with the user's inner spacing intact.
    public void Create_stores_trimmed_display_name_with_inner_spacing()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);

        var p = svc.Create("  Padded   Name  ").Project!;

        Assert.Equal("Padded   Name", p.Name);
        Assert.Equal("Padded   Name", svc.GetById(p.Id)!.Name);
    }

    [Fact]
    public void Update_changes_name_description_and_icon()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Before", "old desc").Project!;

        var result = svc.Update(p.Id, "After", "new desc",
            new IconUpdate(IconUpdateKind.Replace, WriteTempFile(".png", 64)));

        Assert.Null(result.Error);
        Assert.Null(result.IconWarning);
        var updated = svc.GetById(p.Id)!;
        Assert.Equal("After", updated.Name);
        Assert.Equal("new desc", updated.Description);
        Assert.Equal("icon.png", updated.IconFileName);
        Assert.True(File.Exists(updated.IconPath!));
        Assert.EndsWith($"After {p.Id}", Path.GetFileName(updated.FolderPath));
    }

    [Fact]
    public void Update_with_invalid_name_refuses_without_partial_change()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Keep", "keep desc").Project!;
        var folder = p.FolderPath;

        var result = svc.Update(p.Id, "  ", "changed desc",
            new IconUpdate(IconUpdateKind.Replace, WriteTempFile(".png", 64)));

        Assert.Equal(ProjectService.InvalidNameMessage, result.Error);
        Assert.Null(result.IconWarning);
        var unchanged = svc.GetById(p.Id)!;
        Assert.Equal("Keep", unchanged.Name);
        Assert.Equal("keep desc", unchanged.Description);
        Assert.Null(unchanged.IconFileName);
        Assert.Equal(folder, unchanged.FolderPath);
    }

    [Fact]
    public void Update_to_duplicate_name_is_refused_without_partial_change()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        svc.Create("Taken");
        var p = svc.Create("Mine", "mine desc").Project!;

        var result = svc.Update(p.Id, "taken", "changed",
            new IconUpdate(IconUpdateKind.Replace, WriteTempFile(".png", 64)));

        Assert.Equal(ProjectService.DuplicateNameMessage, result.Error);
        Assert.Null(result.IconWarning);
        var unchanged = svc.GetById(p.Id)!;
        Assert.Equal("Mine", unchanged.Name);
        Assert.Equal("mine desc", unchanged.Description);
        Assert.Null(unchanged.IconFileName);
    }

    [Fact] // Keep: an ordinary name/description edit never touches the icon.
    public void Update_keep_leaves_icon_untouched()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Keep Icon", "d", WriteTempFile(".png", 64)).Project!;
        var before = File.ReadAllBytes(p.IconPath!);

        var result = svc.Update(p.Id, "Keep Icon", "new desc", new IconUpdate(IconUpdateKind.Keep));

        Assert.Null(result.Error);
        Assert.Null(result.IconWarning);
        var after = svc.GetById(p.Id)!;
        Assert.Equal("icon.png", after.IconFileName);
        Assert.Equal(before, File.ReadAllBytes(after.IconPath!));
    }

    [Fact] // Remove: the copied file is deleted and the field cleared.
    public void Update_remove_deletes_copied_icon()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Remove Icon", "d", WriteTempFile(".png", 64)).Project!;
        var copied = p.IconPath!;

        var result = svc.Update(p.Id, "Remove Icon", "d", new IconUpdate(IconUpdateKind.Remove));

        Assert.Null(result.Error);
        Assert.Null(result.IconWarning);
        var after = svc.GetById(p.Id)!;
        Assert.Null(after.IconFileName);
        Assert.Null(after.IconPath);
        Assert.False(File.Exists(copied));
    }

    [Fact] // Replace with an unusable image: keep the old icon, warn, but still apply the edit.
    public void Update_replace_with_unusable_image_keeps_icon_and_applies_edit()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Warn Icon", "old", WriteTempFile(".png", 64)).Project!;

        var result = svc.Update(p.Id, "Warn Icon Renamed", "new desc",
            new IconUpdate(IconUpdateKind.Replace, WriteTempFile(".txt", 32)));

        Assert.Null(result.Error);
        Assert.Equal(ProjectService.IconUpdateWarningMessage, result.IconWarning);
        var after = svc.GetById(p.Id)!;
        Assert.Equal("Warn Icon Renamed", after.Name);      // edit was not lost
        Assert.Equal("new desc", after.Description);
        Assert.Equal("icon.png", after.IconFileName);        // existing icon kept
        Assert.True(File.Exists(after.IconPath!));
    }

    [Fact] // Review Focus 1: replacing with the same extension must work and store the new bytes.
    public void Update_replace_same_extension_stores_new_bytes()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Same Ext", "d", WriteTempFile(".png", 64)).Project!;
        var replacement = WriteTempFile(".png", 256);

        var result = svc.Update(p.Id, "Same Ext", "d",
            new IconUpdate(IconUpdateKind.Replace, replacement));

        Assert.Null(result.Error);
        Assert.Null(result.IconWarning);
        var after = svc.GetById(p.Id)!;
        Assert.Equal("icon.png", after.IconFileName);
        Assert.Equal(new FileInfo(replacement).Length, new FileInfo(after.IconPath!).Length);
        Assert.Equal(File.ReadAllBytes(replacement), File.ReadAllBytes(after.IconPath!));
    }

    [Fact] // A failed replacement must leave the old icon intact and change nothing else.
    public void Update_failed_icon_replace_leaves_old_icon_and_nothing_changed()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Locked Replace", "old desc", WriteTempFile(".png", 64)).Project!;
        var oldIcon = p.IconPath!;
        var oldBytes = File.ReadAllBytes(oldIcon);

        // Occupy the intended destination with a directory so the final move of the copy fails.
        Directory.CreateDirectory(Path.Combine(p.FolderPath, "icon.jpg"));

        var result = svc.Update(p.Id, "Locked Replace", "changed desc",
            new IconUpdate(IconUpdateKind.Replace, WriteTempFile(".jpg", 128)));

        Assert.Equal(ProjectService.UpdateFailedMessage, result.Error);
        Assert.Null(result.IconWarning);
        var after = svc.GetById(p.Id)!;
        Assert.Equal("icon.png", after.IconFileName);
        Assert.Equal(oldBytes, File.ReadAllBytes(oldIcon));    // old icon untouched
        Assert.Equal("old desc", after.Description);           // no partial change
        Assert.Equal(p.FolderPath, after.FolderPath);
    }

    [Fact] // Passing the already-copied path as the source is a no-op copy, not a failure.
    public void Update_replace_with_source_already_in_place_succeeds()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("In Place", "d", WriteTempFile(".png", 64)).Project!;

        var result = svc.Update(p.Id, "In Place", "d",
            new IconUpdate(IconUpdateKind.Replace, p.IconPath!));

        Assert.Null(result.Error);
        Assert.Null(result.IconWarning);
        Assert.True(File.Exists(p.IconPath!));
        Assert.Empty(Directory.GetFiles(p.FolderPath, "*.tmp"));
    }

    [Fact] // Ruling 4: a description-only edit must not rename the folder.
    public void Description_only_edit_does_not_rename_folder()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Stable Folder", "old").Project!;
        var folder = p.FolderPath;

        var error = svc.SetDescription(p.Id, "new description");

        Assert.Null(error);
        var after = svc.GetById(p.Id)!;
        Assert.Equal(folder, after.FolderPath);
        Assert.True(Directory.Exists(folder));
        Assert.Equal("new description", after.Description);
    }

    [Fact]
    public void SetIcon_replaces_old_copy_and_removing_clears_it()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Icon Project").Project!;

        Assert.Null(svc.SetIcon(p.Id, WriteTempFile(".png", 64)));
        var withPng = svc.GetById(p.Id)!;
        Assert.Equal("icon.png", withPng.IconFileName);
        Assert.True(File.Exists(Path.Combine(withPng.FolderPath, "icon.png")));

        // A different extension replaces the copy: the old file must be gone.
        Assert.Null(svc.SetIcon(p.Id, WriteTempFile(".jpg", 128)));
        var withJpg = svc.GetById(p.Id)!;
        Assert.Equal("icon.jpg", withJpg.IconFileName);
        Assert.False(File.Exists(Path.Combine(withJpg.FolderPath, "icon.png")));
        Assert.True(File.Exists(Path.Combine(withJpg.FolderPath, "icon.jpg")));

        // null removes the copied file entirely.
        Assert.Null(svc.SetIcon(p.Id, null));
        var cleared = svc.GetById(p.Id)!;
        Assert.Null(cleared.IconFileName);
        Assert.Null(cleared.IconPath);
        Assert.False(File.Exists(Path.Combine(cleared.FolderPath, "icon.jpg")));
    }

    [Fact]
    public void SetIcon_with_unusable_file_leaves_existing_icon()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Icon Keep", "d", WriteTempFile(".png", 64)).Project!;

        var error = svc.SetIcon(p.Id, WriteTempFile(".txt", 32));

        Assert.Equal(ProjectService.IconUpdateWarningMessage, error);
        var still = svc.GetById(p.Id)!;
        Assert.Equal("icon.png", still.IconFileName);
        Assert.True(File.Exists(still.IconPath!));
    }

    [Fact]
    public void Delete_moves_folder_under_backups_with_timestamp()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Disposable", "d", WriteTempFile(".png", 64)).Project!;
        var source = p.FolderPath;
        var folderName = Path.GetFileName(source);

        var error = svc.Delete(p.Id);

        Assert.Null(error);
        Assert.False(Directory.Exists(source));                 // source gone
        Assert.Empty(svc.List());
        var backups = Directory.GetDirectories(Path.Combine(ws.Backups, ProjectService.DeletedProjectsFolderName));
        var backup = Assert.Single(backups);
        Assert.StartsWith(folderName + " (", Path.GetFileName(backup));
        Assert.Matches(@" \(\d{8}-\d{6}\)$", Path.GetFileName(backup));
        Assert.True(File.Exists(Path.Combine(backup, ProjectService.ProjectFileName)));
        Assert.True(File.Exists(Path.Combine(backup, "icon.png"))); // folder moved whole
    }

    [Fact]
    public void Delete_twice_with_same_name_gets_distinct_destinations()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var first = svc.Create("Twice").Project!;
        Assert.Null(svc.Delete(first.Id));

        // Unique names apply only while a project exists; once deleted the name is free again.
        var second = svc.Create("Twice").Project!;
        Assert.Null(svc.Delete(second.Id));

        var backups = Directory.GetDirectories(Path.Combine(ws.Backups, ProjectService.DeletedProjectsFolderName));
        Assert.Equal(2, backups.Length);
        Assert.Equal(2, backups.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact] // Guard: a failure moving the folder leaves the project intact and returns a plain message.
    public void Delete_failure_leaves_project_intact()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Cannot Delete").Project!;

        // Make the backups root unusable: replace the folder with a file of the same name.
        Directory.Delete(ws.Backups, recursive: true);
        File.WriteAllText(ws.Backups, "not a folder");

        var error = svc.Delete(p.Id);

        Assert.Equal(ProjectService.DeleteFailedMessage, error);
        Assert.True(Directory.Exists(p.FolderPath));            // project stays intact
        Assert.Single(svc.List());
    }

    [Fact]
    public void Mutations_on_unknown_id_return_not_found()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);

        Assert.Equal(ProjectService.NotFoundMessage, svc.Rename("nope", "New"));
        Assert.Equal(ProjectService.NotFoundMessage, svc.SetDescription("nope", "x"));
        Assert.Equal(ProjectService.NotFoundMessage, svc.SetIcon("nope", null));
        Assert.Equal(ProjectService.NotFoundMessage, svc.RemoveIcon("nope"));
        Assert.Equal(ProjectService.NotFoundMessage,
            svc.Update("nope", "New", "x", new IconUpdate(IconUpdateKind.Keep)).Error);
        Assert.Equal(ProjectService.NotFoundMessage, svc.Delete("nope"));
    }

    // ---- Final-review fix 1: grandfathered capitalisation-duplicate projects stay editable --------

    /// <summary>
    /// Seed a workspace that already holds two projects whose names differ only by capitalisation —
    /// exactly the situation the owner hit ("Inventory adjustments" / "Inventory Adjustments").
    /// Written straight to disk because <see cref="ProjectService.Create"/> would (correctly) refuse
    /// the second name.
    /// </summary>
    private static (WorkspacePaths Ws, ProjectService Svc) SeedCaseVariantTwins()
    {
        var ws = TempWs();
        var utc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedProject(ws, "twin0001", "Inventory adjustments", utc);
        SeedProject(ws, "twin0002", "Inventory Adjustments", utc);
        return (ws, new ProjectService(ws));
    }

    [Fact] // a description-only edit must succeed even when a case-variant twin already exists
    public void Update_description_only_succeeds_with_existing_case_variant_twin()
    {
        var (_, svc) = SeedCaseVariantTwins();
        var target = svc.List().Single(p => p.Name == "Inventory adjustments");

        var result = svc.Update(target.Id, "Inventory adjustments", "changed description",
            new IconUpdate(IconUpdateKind.Keep));

        Assert.Null(result.Error);
        Assert.Null(result.IconWarning);
        var after = svc.GetById(target.Id)!;
        Assert.Equal("changed description", after.Description);
        Assert.Equal("Inventory adjustments", after.Name);
    }

    [Fact] // RemoveIcon (a name-unchanged icon edit) must succeed with a case-variant twin present
    public void RemoveIcon_succeeds_with_existing_case_variant_twin()
    {
        var (_, svc) = SeedCaseVariantTwins();
        var target = svc.List().Single(p => p.Name == "Inventory adjustments");

        var error = svc.RemoveIcon(target.Id);

        Assert.Null(error);
        Assert.Null(svc.GetById(target.Id)!.IconFileName);
    }

    [Fact] // a case-only self-rename must succeed (and rename the folder), not be refused as duplicate
    public void Case_only_self_rename_succeeds_with_existing_case_variant_twin()
    {
        var (ws, svc) = SeedCaseVariantTwins();
        var target = svc.List().Single(p => p.Name == "Inventory adjustments");

        var error = svc.Rename(target.Id, "INVENTORY ADJUSTMENTS");

        Assert.Null(error);
        var after = svc.GetById(target.Id)!;
        Assert.Equal("INVENTORY ADJUSTMENTS", after.Name);
        Assert.Equal(target.Id, after.Id);
        // The folder keeps the same id; the on-disk casing is the new one (Windows paths are
        // case-insensitive, so existence alone can't prove the rename — check the shown casing).
        var onDisk = Directory.GetDirectories(ws.Projects)
            .Single(d => Path.GetFileName(d).EndsWith(target.Id, StringComparison.Ordinal));
        Assert.Equal($"INVENTORY ADJUSTMENTS {target.Id}", Path.GetFileName(onDisk));
        Assert.Equal(onDisk, after.FolderPath);
    }

    [Fact] // adopting another project's (normalised) name is still refused
    public void Rename_to_existing_case_variant_name_is_still_refused()
    {
        var (_, svc) = SeedCaseVariantTwins();
        var alpha = svc.Create("Alpha").Project!;
        var folder = alpha.FolderPath;

        var error = svc.Rename(alpha.Id, "inventory   ADJUSTMENTS");

        Assert.Equal(ProjectService.DuplicateNameMessage, error);
        var still = svc.GetById(alpha.Id)!;
        Assert.Equal("Alpha", still.Name);
        Assert.Equal(folder, still.FolderPath);
    }

    // ---- Final-review fix 2: a failed project.json write must not destroy the icon ----------------

    [Fact]
    public void Update_failed_json_write_leaves_icon_untouched()
    {
        var ws = TempWs(); var svc = new ProjectService(ws);
        var p = svc.Create("Icon Safe", "old desc", WriteTempFile(".png", 64)).Project!;
        var jsonPath = Path.Combine(p.FolderPath, ProjectService.ProjectFileName);
        var jsonBefore = File.ReadAllText(jsonPath);
        var iconPath = p.IconPath!;
        var iconBefore = File.ReadAllBytes(iconPath);

        // A read-only project.json can still be read (so the service resolves the project) but cannot
        // be overwritten: the JSON write fails before the icon step is ever reached.
        File.SetAttributes(jsonPath, FileAttributes.ReadOnly);
        try
        {
            var result = svc.Update(p.Id, "Icon Safe", "new desc",
                new IconUpdate(IconUpdateKind.Replace, WriteTempFile(".jpg", 128)));

            Assert.Equal(ProjectService.UpdateFailedMessage, result.Error);
            Assert.Null(result.IconWarning);
        }
        finally
        {
            File.SetAttributes(jsonPath, FileAttributes.Normal);
        }

        var after = svc.GetById(p.Id)!;
        Assert.Equal("old desc", after.Description);              // nothing saved
        Assert.Equal("icon.png", after.IconFileName);
        Assert.True(File.Exists(iconPath));
        Assert.False(File.Exists(Path.Combine(p.FolderPath, "icon.jpg"))); // the icon step never ran
        Assert.Equal(iconBefore, File.ReadAllBytes(iconPath));    // bytes exactly as before
        Assert.Equal(jsonBefore, File.ReadAllText(jsonPath));
    }
}
