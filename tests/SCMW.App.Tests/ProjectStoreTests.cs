using Avalonia.Headless.XUnit;
using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App.Tests;

public class ProjectStoreTests
{
    private static (ProjectStore store, WorkspacePaths ws, string root) NewStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-store-" + Guid.NewGuid().ToString("N"));
        var ws = WorkspaceService.Ensure(root);
        var store = new ProjectStore(new ProjectService(ws), new JsonSettingsStore(root));
        return (store, ws, root);
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [AvaloniaFact]
    public void Creating_a_project_puts_it_in_the_workspace()
    {
        var (store, ws, root) = NewStore();
        try
        {
            var result = store.Create("My first mod", "a description");

            Assert.NotNull(result.Project);
            var project = Assert.Single(store.All());
            Assert.Equal("My first mod", project.Name);
            Assert.Equal("a description", project.Description);
            Assert.True(Directory.Exists(project.FolderPath));
            Assert.True(File.Exists(Path.Combine(project.FolderPath, "project.json")));
            Assert.True(Directory.EnumerateDirectories(ws.Projects).Any());
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_duplicate_name_is_refused_with_a_plain_message()
    {
        var (store, _, root) = NewStore();
        try
        {
            store.Create("Same", "");
            var again = store.Create("Same", "");

            Assert.Null(again.Project);
            Assert.False(string.IsNullOrWhiteSpace(again.ErrorMessage));
            Assert.Single(store.All());
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Rename_and_description_stick()
    {
        var (store, _, root) = NewStore();
        try
        {
            var created = store.Create("Old name", "before").Project!;

            Assert.Null(store.Rename(created.Id, "New name"));
            Assert.Null(store.SetDescription(created.Id, "after"));

            var project = store.All().Single(p => p.Id == created.Id);
            Assert.Equal("New name", project.Name);
            Assert.Equal("after", project.Description);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_current_project_is_saved_and_read_back()
    {
        var (store, _, root) = NewStore();
        try
        {
            var first = store.Create("First", "").Project!;
            var second = store.Create("Second", "").Project!;

            store.SetCurrent(second.Id);

            Assert.Equal(second.Id, store.Current!.Id);

            // A brand-new store over the same workspace remembers it.
            var again = new ProjectStore(
                new ProjectService(WorkspaceService.Ensure(root)), new JsonSettingsStore(root));
            Assert.Equal(second.Id, again.Current!.Id);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_current_project_is_a_fallback_until_one_is_chosen()
    {
        var (store, _, root) = NewStore();
        try
        {
            var first = store.Create("First", "").Project!;
            var second = store.Create("Second", "").Project!;
            store.SetCurrent(second.Id);

            Assert.False(store.CurrentIsFallback);          // explicitly chosen

            store.Delete(second.Id);                        // the chosen one goes away

            Assert.True(store.CurrentIsFallback);           // "First" now stands in for it
            Assert.Equal(first.Id, store.Current!.Id);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Deleting_moves_the_folder_into_the_backups()
    {
        var (store, ws, root) = NewStore();
        try
        {
            var project = store.Create("Doomed", "").Project!;

            Assert.Null(store.Delete(project.Id));

            Assert.Empty(store.All());
            Assert.False(Directory.Exists(project.FolderPath));
            var deleted = Path.Combine(ws.Backups, ProjectService.DeletedProjectsFolderName);
            Assert.True(Directory.Exists(deleted));
            Assert.NotEmpty(Directory.EnumerateDirectories(deleted));
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void Duplicate_makes_a_copy()
    {
        var (store, _, root) = NewStore();
        try
        {
            var project = store.Create("Original", "same").Project!;

            var copy = store.Duplicate(project.Id);

            Assert.NotNull(copy.Project);
            Assert.Equal("Original copy", copy.Project!.Name);
            Assert.Equal("same", copy.Project.Description);
            Assert.Equal(2, store.All().Count);
        }
        finally { Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_list_is_newest_first_and_changes_are_announced()
    {
        var (store, _, root) = NewStore();
        try
        {
            var sawChange = 0;
            store.Changed += () => sawChange++;

            store.Create("One", "");
            store.Create("Two", "");

            Assert.True(sawChange >= 2);
            var all = store.All();
            Assert.True(all[0].ModifiedUtc >= all[^1].ModifiedUtc);
        }
        finally { Cleanup(root); }
    }
}
