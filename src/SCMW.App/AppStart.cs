using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App;

/// <summary>
/// First-run preparation: resolve the workspace, write the settings, create the folders.
/// Idempotent — running it twice changes nothing.
/// </summary>
public static class AppStart
{
    public static (AppSettings Settings, WorkspacePaths Workspace) Prepare(
        AppPaths paths, ISettingsStore store)
    {
        var settings = store.Load();

        if (string.IsNullOrWhiteSpace(settings.WorkspacePath))
        {
            settings = settings with { WorkspacePath = paths.DefaultWorkspace };
            store.Save(settings);
        }

        var workspace = WorkspaceService.Ensure(settings.WorkspacePath!);
        return (settings, workspace);
    }
}
