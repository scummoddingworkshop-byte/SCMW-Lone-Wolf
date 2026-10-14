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
            Assert.Contains(texts, t => t is not null && t.Contains("Duct Tape") && t.Contains("25"));
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

            // Open actually reaches the view model and navigates.
            var openButton = tile.FindControl<Button>("OpenButton")!;
            Assert.NotNull(openButton.Command);
            openButton.Command!.Execute(openButton.CommandParameter);

            Assert.Equal(ScreenKey.Workspace, nav.Current);
        }
        finally { TestProjects.Cleanup(root); }
    }
}
