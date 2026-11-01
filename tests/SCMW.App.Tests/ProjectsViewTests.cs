using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;
using SCMW.Core.Editing;
using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App.Tests;

public class ProjectsViewTests
{
    private static (ProjectsView view, ProjectsViewModel vm, string root) Shown(int projectCount = 0)
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pv-" + Guid.NewGuid().ToString("N"));
        var store = new ProjectStore(
            new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());

        for (var i = 0; i < projectCount; i++)
        {
            store.Create($"Project {i + 1}", $"description {i + 1}");
        }

        var vm = new ProjectsViewModel(store, new NavigationHost());
        var view = new ProjectsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 900 };
        window.Show();
        return (view, vm, root);
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [AvaloniaFact] // every project card carries its own version box and Build button
    public void Each_project_card_carries_a_version_box_and_a_build_button()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());
            store.Create("Tape mod", "");
            new ProjectService(WorkspaceService.Ensure(root)).SetVersion(store.All()[0].Id, "2.1.0");

            var build = new BuildViewModel(projects: store);
            var vm = new ProjectsViewModel(store, new NavigationHost(), build: build);
            var view = new ProjectsView { DataContext = vm };
            new Window { Content = view, Width = 1100, Height = 900 }.Show();

            var boxes = view.GetVisualDescendants().OfType<TextBox>()
                .Where(t => t.Text == "2.1.0").ToList();
            Assert.Single(boxes);                                   // the card's own version box

            Assert.Contains(view.GetVisualDescendants().OfType<Button>(),
                b => (b.Content as string) == "Build");
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // an item's card can drop every change to it, in one press
    public void A_change_card_has_a_remove_button()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id,
                               assetPath: "/a/Duct_Tape_ES");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);
            var view = new ProjectsView { DataContext = vm };
            new Window { Content = view, Width = 1100, Height = 900 }.Show();

            Assert.Contains(view.GetVisualDescendants().OfType<Button>(),
                b => (b.Content as string) == "Remove");
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // the build strip shows its steps while a build runs
    public void The_build_strip_shows_the_step_bar_while_running()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id,
                               assetPath: "/a/Duct_Tape_ES");

            var build = new BuildViewModel(changes, projects: store);
            var screen = new ProjectsViewModel(store, new NavigationHost(), changes, build: build);
            var view = new ProjectsView { DataContext = screen };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            build.BuildCommand.Execute(null);   // starts the simulated run
            window.UpdateLayout();

            Assert.True(view.FindControl<StackPanel>("StepBar")!.IsVisible);
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Prepare", texts);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact] // and its success and failure panels, exactly as the old Build screen had them
    public void The_build_strip_shows_success_and_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id,
                               assetPath: "/a/Duct_Tape_ES");

            var build = new BuildViewModel(changes, projects: store);
            var screen = new ProjectsViewModel(store, new NavigationHost(), changes, build: build);
            var view = new ProjectsView { DataContext = screen };
            new Window { Content = view, Width = 1100, Height = 900 }.Show();

            build.BuildCommand.Execute(null);
            for (var i = 0; i < 5; i++) build.Advance();
            Assert.True(view.FindControl<StackPanel>("SuccessPanel")!.IsVisible);

            build.ShowFailure("Nope", "Because", "Try again", "details");
            Assert.True(view.FindControl<StackPanel>("FailurePanel")!.IsVisible);
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Try again", texts);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Projects_shows_the_header_search_and_grid()
    {
        var (view, _, root) = Shown(2);
        try
        {
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

            Assert.Contains("Projects", texts);
            Assert.NotNull(view.FindControl<TextBox>("SearchBox"));
            Assert.Contains("New project", texts);
            Assert.Contains("Project 1", texts);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void An_empty_workspace_shows_the_empty_state()
    {
        var (view, _, root) = Shown();
        try
        {
            Assert.True(view.FindControl<Border>("EmptyState")!.IsVisible);
        }
        finally { Cleanup(root); }

        var (withProjects, _, root2) = Shown(2);
        try
        {
            Assert.False(withProjects.FindControl<Border>("EmptyState")!.IsVisible);
        }
        finally { Cleanup(root2); }
    }

    [AvaloniaFact] // "one project = one mod": the chosen project's own change list shows on screen
    public void The_chosen_projects_change_list_is_shown()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-pv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());
            var project = store.Create("Tape mod", "").Project!;
            store.SetCurrent(project.Id);

            var changes = new ItemChangeStore(new ProjectChangeStore(), store);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id,
                               assetPath: "/a/Duct_Tape");

            var vm = new ProjectsViewModel(store, new NavigationHost(), changes);
            var view = new ProjectsView { DataContext = vm };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            var texts = view.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text).ToList();

            Assert.Contains("What this mod changes", texts);
            Assert.Contains("Duct Tape", texts);                       // the friendly name, like Build
            Assert.Contains(vm.Changes[0].Rows[0].Field, texts);        // the value that changed, in words
            Assert.Contains("10 → 25", texts);                          // game's number → what it became
            Assert.Contains("1 change", texts);                        // how many on that item
            Assert.DoesNotContain(texts, t => t is not null && t.Contains("/a/Duct_Tape"));
            Assert.DoesNotContain("Duct_Tape", texts);                 // the file name is not the heading
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_panel_opens_and_closes()
    {
        var (view, vm, root) = Shown();
        try
        {
            Assert.False(view.FindControl<Border>("Panel")!.IsVisible);

            vm.NewProjectCommand.Execute(null);
            Assert.True(view.FindControl<Border>("Panel")!.IsVisible);

            vm.CancelEditCommand.Execute(null);
            Assert.False(view.FindControl<Border>("Panel")!.IsVisible);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_search_with_no_matches_says_so()
    {
        var (view, vm, root) = Shown(2);
        try
        {
            vm.Search = "zzz-no-match";

            Assert.True(view.FindControl<Border>("NoMatches")!.IsVisible);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_panel_includes_a_picture_slot()
    {
        var (view, vm, root) = Shown();
        try
        {
            vm.NewProjectCommand.Execute(null);

            Assert.NotNull(view.FindControl<Border>("PanelPicture"));
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_panel_says_rename_when_renaming()
    {
        var (view, vm, root) = Shown(1);
        try
        {
            var heading = view.FindControl<TextBlock>("PanelTitleText")!;
            var action = view.FindControl<Button>("PanelActionButton")!;

            vm.NewProjectCommand.Execute(null);
            Assert.Equal("New project", heading.Text);
            Assert.Equal("Create", action.Content);

            vm.RenameProjectCommand.Execute(vm.VisibleProjects[0]);
            Assert.Equal("Edit project", heading.Text);
            Assert.Equal("Save", action.Content);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_tiles_are_wired_to_the_view_model_commands()
    {
        var (store, root) = TestProjects.With("A project");
        try
        {
            var nav = new NavigationHost();
            var vm = new ProjectsViewModel(store, nav);
            var view = new ProjectsView { DataContext = vm };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            var tile = view.GetVisualDescendants().OfType<ProjectTile>().First();

            Assert.Same(vm.OpenProjectCommand, tile.OpenCommand);
            Assert.Same(vm.RenameProjectCommand, tile.RenameCommand);
            Assert.Same(vm.DuplicateProjectCommand, tile.DuplicateCommand);
            Assert.Same(vm.RequestDeleteCommand, tile.RequestDeleteCommand);
            Assert.Same(vm.ConfirmDeleteCommand, tile.ConfirmDeleteCommand);
            Assert.Same(vm.CancelDeleteCommand, tile.CancelDeleteCommand);

            // Picking the card reaches the view model and stays on this screen.
            Assert.True(tile.OpenCommand!.CanExecute(tile.Project));
            var picked = tile.Project!;
            tile.OpenCommand.Execute(picked);

            Assert.Equal(ScreenKey.Projects, nav.Current);
            Assert.True(vm.VisibleProjects.Single(p => p.Id == picked.Id).IsCurrent);   // card says "Current"
        }
        finally { TestProjects.Cleanup(root); }
    }
}
