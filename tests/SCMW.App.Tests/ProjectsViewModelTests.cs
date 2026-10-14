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
    public void Open_makes_it_current_and_goes_to_the_workspace()
    {
        var (vm, store, nav, root) = NewVm(2);
        try
        {
            var project = vm.VisibleProjects[0];

            vm.OpenProjectCommand.Execute(project);

            Assert.Equal(project.Id, store.Current!.Id);
            Assert.Equal(ScreenKey.Workspace, nav.Current);   // editing lives in the Workspace now
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

            // The chosen project's list is the one shown (here B, one change).
            var row = Assert.Single(vm.Changes);
            Assert.Equal("Batteries", row.Asset);
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
