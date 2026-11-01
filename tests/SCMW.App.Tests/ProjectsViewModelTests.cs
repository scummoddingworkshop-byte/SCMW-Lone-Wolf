using Avalonia.Headless.XUnit;
using SCMW.Core.Editing;
using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App.Tests;

public class ProjectsViewModelTests
{
    private static (ProjectsViewModel vm, ProjectStore store, NavigationHost nav, string root) NewVm(
        int projectCount = 0)
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        var workspace = WorkspaceService.Ensure(root);
        var store = new ProjectStore(new ProjectService(workspace), new JsonSettingsStore(root));

        for (var i = 0; i < projectCount; i++)
        {
            store.Create($"Project {i + 1}", $"description {i + 1}");
        }

        var nav = new NavigationHost();
        return (new ProjectsViewModel(store, nav), store, nav, root);
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [AvaloniaFact]
    public void A_refused_duplicate_is_shown_outside_the_panel()
    {
        var (vm, store, _, root) = NewVm(1);
        try
        {
            var project = vm.VisibleProjects.Single();
            vm.DuplicateProjectCommand.Execute(project);       // makes "Project 1 copy"
            vm.DuplicateProjectCommand.Execute(project);       // refused: that name already exists

            Assert.True(vm.ShowListMessage);
            Assert.False(vm.IsPanelOpen);                      // visible even with the panel closed
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void An_empty_workspace_shows_the_empty_state()
    {
        var (vm, _, _, root) = NewVm();
        try
        {
            Assert.False(vm.HasProjects);
            Assert.True(vm.ShowEmptyState);
            Assert.Empty(vm.VisibleProjects);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_list_comes_from_the_workspace()
    {
        var (vm, store, _, root) = NewVm(3);
        try
        {
            Assert.True(vm.HasProjects);
            Assert.Equal(3, vm.VisibleProjects.Count);
            Assert.False(vm.ShowEmptyState);

            // Nothing is marked as "your mod" until the player picks one — no quiet fall-back.
            Assert.DoesNotContain(vm.VisibleProjects, p => p.IsCurrent);

            var chosen = store.All()[1];
            store.SetCurrent(chosen.Id);

            var marked = Assert.Single(vm.VisibleProjects.Where(p => p.IsCurrent));
            Assert.Equal(chosen.Id, marked.Id);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Create_adds_a_real_project_and_makes_it_current()
    {
        var (vm, store, _, root) = NewVm(1);
        try
        {
            vm.NewProjectCommand.Execute(null);
            Assert.True(vm.IsPanelOpen);
            Assert.False(vm.IsRenaming);

            vm.DraftName = "My new mod";
            vm.DraftDescription = "made in a test";
            vm.CommitEditCommand.Execute(null);

            Assert.False(vm.IsPanelOpen);
            Assert.Contains(vm.VisibleProjects, p => p.Name == "My new mod");
            Assert.Equal("My new mod", store.Current!.Name);
            Assert.Equal("made in a test", store.Current.Description);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Rename_prefills_and_applies()
    {
        var (vm, store, _, root) = NewVm(1);
        try
        {
            var first = vm.VisibleProjects[0];

            vm.RenameProjectCommand.Execute(first);
            Assert.True(vm.IsRenaming);
            Assert.Equal(first.Name, vm.DraftName);

            vm.DraftName = "Renamed";
            vm.CommitEditCommand.Execute(null);

            Assert.Equal("Renamed", store.All().Single(p => p.Id == first.Id).Name);
            Assert.Contains(vm.VisibleProjects, p => p.Name == "Renamed");
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Duplicate_adds_a_copy()
    {
        var (vm, _, _, root) = NewVm(1);
        try
        {
            vm.DuplicateProjectCommand.Execute(vm.VisibleProjects[0]);

            Assert.Equal(2, vm.VisibleProjects.Count);
            Assert.Contains(vm.VisibleProjects, p => p.Name.EndsWith(" copy"));
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Delete_asks_first_then_removes()
    {
        var (vm, _, _, root) = NewVm(2);
        try
        {
            var project = vm.VisibleProjects[0];

            vm.RequestDeleteCommand.Execute(project);
            Assert.True(project.IsConfirmingDelete);
            Assert.Contains(project, vm.VisibleProjects);   // nothing removed yet

            vm.CancelDeleteCommand.Execute(project);
            Assert.False(project.IsConfirmingDelete);
            Assert.Contains(project, vm.VisibleProjects);

            vm.RequestDeleteCommand.Execute(project);
            vm.ConfirmDeleteCommand.Execute(project);

            Assert.DoesNotContain(vm.VisibleProjects, p => p.Id == project.Id);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Open_makes_it_current_and_stays_on_the_projects_screen()
    {
        var (vm, store, nav, root) = NewVm(2);
        try
        {
            var project = vm.VisibleProjects[0];

            vm.OpenProjectCommand.Execute(project);

            Assert.Equal(project.Id, store.Current!.Id);
            Assert.Equal(ScreenKey.Projects, nav.Current);   // picking a mod never throws you out
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // a change's value is named the way the Modify screen names it
    public void A_changed_value_is_named_in_plain_words_not_its_technical_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "GridInventoryRowSpan", "2", "1", project.Id,
                               assetPath: "/a/Duct_Tape_ES");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);
            var row = Assert.Single(Assert.Single(vm.Changes).Rows);

            Assert.Equal("Height in grid", row.Field);               // the words the Modify screen uses
            Assert.Equal("GridInventoryRowSpan", row.FieldTechnical); // the game's own name stays for the tip
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // clicking a change card asks the shell to open that item
    public void Opening_a_change_card_asks_for_that_item()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id,
                               assetPath: "/a/Duct_Tape_ES");

            string? asked = null;
            var vm = new ProjectsViewModel(store, new NavigationHost(), changes,
                                           openItem: path => asked = path);

            vm.OpenItemCommand.Execute(Assert.Single(vm.Changes));

            Assert.Equal("/a/Duct_Tape_ES", asked);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // "one project = one mod": each project holds its own change list
    public void Two_projects_keep_their_own_change_lists()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var a = store.Create("Project A", "").Project!;
            var b = store.Create("Project B", "").Project!;
            store.SetCurrent(b.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", a.Id, assetPath: "/a/Duct_Tape");
            changes.RecordEdit("AK-15", "Damage", "45", "60", a.Id, assetPath: "/a/AK");
            changes.RecordEdit("Batteries", "Weight", "400", "800", b.Id, assetPath: "/b/Bat");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);

            var rowA = vm.VisibleProjects.Single(p => p.Name == "Project A");
            var rowB = vm.VisibleProjects.Single(p => p.Name == "Project B");
            Assert.Equal(2, rowA.ChangeCount);          // A has its own two
            Assert.Equal(1, rowB.ChangeCount);          // B has its own one

            // The chosen project's list is the one shown (here B, one change, one card).
            var group = Assert.Single(vm.Changes);
            Assert.Equal("Bat_ES", group.FileName);           // the item file, the same one Build opens
            Assert.Equal("/b/Bat_ES", Assert.Single(group.Rows).Path);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // the project's card counts what the list shows, not the old edits behind it
    public void A_project_counts_current_values_not_old_edits()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Duct Tape", "Max stack", "25", "50", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Duct Tape", "Max stack", "50", "75", project.Id, assetPath: "/a/Duct_Tape_ES");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);

            Assert.Equal(1, vm.VisibleProjects.Single().ChangeCount);
            Assert.Equal("1 change", Assert.Single(vm.Changes).ChangeCountText);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // editing one value more than once shows one line, with the latest value
    public void A_value_edited_more_than_once_shows_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Duct Tape", "Max stack", "25", "50", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Duct Tape", "Max stack", "50", "75", project.Id, assetPath: "/a/Duct_Tape_ES");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);

            // One card, one line: the game's own number → the number it will have.
            var row = Assert.Single(Assert.Single(vm.Changes).Rows);
            Assert.Equal("Max Stack", row.Field);
            Assert.Equal("10 → 75", row.Value);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // undo clears the whole value, not just its newest edit
    public void Undoing_a_value_removes_every_edit_to_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Duct Tape", "Max stack", "25", "50", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Duct Tape", "Weight", "5", "3", project.Id, assetPath: "/a/Duct_Tape_ES");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);
            var group = Assert.Single(vm.Changes);
            var stackRow = group.Rows.Single(r => r.FieldTechnical == "Max stack");

            vm.UndoChangeCommand.Execute(stackRow);

            var left = Assert.Single(Assert.Single(vm.Changes).Rows);
            Assert.Equal("Weight", left.FieldTechnical);               // the other value stayed
            var folder = store.All().Single(p => p.Id == project.Id).FolderPath;
            Assert.Single(changes.For(folder));                        // both stack edits went
            Assert.Equal(1, vm.VisibleProjects.Single(p => p.Id == project.Id).ChangeCount);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // undo removes exactly that value, not the whole item
    public void Undoing_a_change_removes_exactly_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id, assetPath: "/a/Duct_Tape");
            changes.RecordEdit("Duct Tape", "Weight", "5", "3", project.Id, assetPath: "/a/Duct_Tape");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);

            // One card per item, holding all of that item's changes.
            var group = Assert.Single(vm.Changes);
            Assert.Equal("Duct_Tape_ES", group.FileName);
            Assert.Equal("Duct Tape", group.FriendlyName);      // the same words Build's list uses
            Assert.Equal("2 changes", group.ChangeCountText);
            Assert.Equal(2, group.Rows.Count);

            var first = group.Rows[0];
            vm.UndoChangeCommand.Execute(first);

            group = Assert.Single(vm.Changes);
            var left = Assert.Single(group.Rows);
            Assert.NotEqual(first.FieldTechnical, left.FieldTechnical);   // the other value stayed
            Assert.Equal("1 change", group.ChangeCountText);
            var folder = store.All().Single(p => p.Id == project.Id).FolderPath;
            Assert.DoesNotContain(changes.For(folder),
                                  r => r.FieldTechnicalName == first.FieldTechnical);
            Assert.Equal(1, vm.VisibleProjects.Single(p => p.Id == project.Id).ChangeCount);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // each card builds its own mod, remembering that mod's version
    public void Building_from_a_card_makes_it_current_and_uses_its_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var tape = store.Create("Tape mod", "").Project!;
            var other = store.Create("Other mod", "").Project!;
            store.SetCurrent(other.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", tape.Id, assetPath: "/a/Duct_Tape_ES");

            var service = new ProjectService(WorkspaceService.Ensure(root));
            service.SetVersion(tape.Id, "2.4.1");

            var build = new BuildViewModel(changes, projects: store);
            var vm = new ProjectsViewModel(store, new NavigationHost(), changes, build: build);

            vm.BuildProjectCommand.Execute(vm.VisibleProjects.Single(p => p.Name == "Tape mod"));

            Assert.Equal(tape.Id, store.Chosen!.Id);      // the mod being built is the current one
            Assert.Equal("2.4.1", build.ModVersion);      // with the version the card remembered
            Assert.True(build.IsRunning);                 // and the build started
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // Remove on an item card drops every change to that item, and only that item
    public void Remove_on_an_item_card_drops_that_items_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("Bolts", "Weight", "5", "3", project.Id, assetPath: "/a/Bolts_ES");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);
            var tape = vm.Changes.Single(c => c.FriendlyName == "Duct Tape");

            vm.RemoveItemCommand.Execute(tape);

            var left = Assert.Single(vm.Changes);
            Assert.Equal("Bolts", left.FriendlyName);
            var folder = store.All().Single(p => p.Id == project.Id).FolderPath;
            Assert.DoesNotContain(changes.For(folder), r => r.AssetPath!.Contains("Duct_Tape"));
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // a card keeps its own version between visits
    public void A_card_shows_the_projects_saved_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pvm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            var project = store.Create("Tape mod", "").Project!;
            new ProjectService(WorkspaceService.Ensure(root)).SetVersion(project.Id, "3.2.0");

            var vm = new ProjectsViewModel(store, new NavigationHost(), null,
                                           build: new BuildViewModel(projects: store));

            Assert.Equal("3.2.0", vm.VisibleProjects.Single().ModVersion);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Search_filters_and_restores()
    {
        var (vm, _, _, root) = NewVm(3);
        try
        {
            vm.Search = "Project 2";
            Assert.Single(vm.VisibleProjects);

            vm.Search = "zzz-no-match";
            Assert.Empty(vm.VisibleProjects);
            Assert.True(vm.ShowNoMatches);

            vm.Search = "";
            Assert.Equal(3, vm.VisibleProjects.Count);
            Assert.False(vm.ShowNoMatches);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_duplicate_name_is_shown_in_the_panel()
    {
        var (vm, _, _, root) = NewVm(1);
        try
        {
            vm.NewProjectCommand.Execute(null);
            vm.DraftName = "Project 1";               // already taken
            vm.CommitEditCommand.Execute(null);

            Assert.False(string.IsNullOrWhiteSpace(vm.PanelError));
            Assert.True(vm.IsPanelOpen);              // the panel stays so it can be corrected
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_missing_name_is_refused_politely()
    {
        var (vm, _, _, root) = NewVm();
        try
        {
            vm.NewProjectCommand.Execute(null);
            vm.DraftName = "   ";
            vm.CommitEditCommand.Execute(null);

            Assert.False(string.IsNullOrWhiteSpace(vm.PanelError));
            Assert.True(vm.ShowEmptyState);           // still nothing created
        }
        finally { Cleanup(root); }
    }

    // A real (tiny) 1x1 PNG, exactly what the engine would copy in.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [AvaloniaFact]
    public void A_picture_can_be_chosen_when_creating_a_project()
    {
        var (vm, store, _, root) = NewVm();
        try
        {
            var png = Path.Combine(root, "chosen.png");
            File.WriteAllBytes(png, Convert.FromBase64String(TinyPngBase64));

            vm.NewProjectCommand.Execute(null);
            vm.DraftName = "With a picture";
            vm.SetDraftIcon(png);
            Assert.NotNull(vm.DraftPicture);           // the panel preview loads it

            vm.CommitEditCommand.Execute(null);

            var project = store.All().Single(p => p.Name == "With a picture");
            Assert.NotNull(project.IconPath);          // the file was copied in
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_picture_can_be_added_then_removed_on_an_existing_project()
    {
        var (vm, store, _, root) = NewVm(1);
        try
        {
            var png = Path.Combine(root, "one.png");
            File.WriteAllBytes(png, Convert.FromBase64String(TinyPngBase64));
            var id = vm.VisibleProjects.Single().Id;

            vm.RenameProjectCommand.Execute(vm.VisibleProjects.Single());
            vm.SetDraftIcon(png);
            vm.CommitEditCommand.Execute(null);
            Assert.NotNull(store.All().Single(p => p.Id == id).IconPath);

            vm.RenameProjectCommand.Execute(vm.VisibleProjects.Single(p => p.Id == id));
            vm.RemovePictureCommand.Execute(null);
            vm.CommitEditCommand.Execute(null);
            Assert.Null(store.All().Single(p => p.Id == id).IconPath);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Renaming_without_touching_the_picture_keeps_it()
    {
        var (vm, store, _, root) = NewVm(1);
        try
        {
            var png = Path.Combine(root, "keep.png");
            File.WriteAllBytes(png, Convert.FromBase64String(TinyPngBase64));
            var id = vm.VisibleProjects.Single().Id;

            vm.RenameProjectCommand.Execute(vm.VisibleProjects.Single());
            vm.SetDraftIcon(png);
            vm.CommitEditCommand.Execute(null);

            vm.RenameProjectCommand.Execute(vm.VisibleProjects.Single(p => p.Id == id));
            Assert.NotNull(vm.DraftPicture);           // the existing picture is shown
            vm.DraftName = "Renamed only";
            vm.CommitEditCommand.Execute(null);

            var project = store.All().Single(p => p.Id == id);
            Assert.Equal("Renamed only", project.Name);
            Assert.NotNull(project.IconPath);          // untouched picture survives
        }
        finally { Cleanup(root); }
    }
}
