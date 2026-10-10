using SCMW.Core.Editing;
using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App.Tests;

/// <summary>
/// Throwaway workspaces for tests — a temp folder with a real project store, so nothing ever
/// touches the real documents folder. Clean up with <see cref="Cleanup"/>.
/// </summary>
internal static class TestProjects
{
    public static (ProjectStore Store, string Root) With(params string[] names)
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-test-" + Guid.NewGuid().ToString("N"));
        var store = new ProjectStore(
            new ProjectService(WorkspaceService.Ensure(root)), new InMemorySettingsStore());

        foreach (var name in names) store.Create(name, "");
        if (names.Length > 0) store.SetCurrent(store.All()[0].Id);

        return (store, root);
    }

    public static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    /// <summary>An item-change store over the given projects (a throwaway workspace in tests).</summary>
    public static ItemChangeStore ChangeStore(ProjectStore store) =>
        new(new ProjectChangeStore(), store);
}
