using Avalonia.Headless.XUnit;
using SCMW.Core.Settings;

namespace SCMW.App.Tests;

public class AppStartTests
{
    [AvaloniaFact]
    public void First_run_writes_settings_and_creates_the_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-start-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "appdata"), Path.Combine(root, "docs", "SCMW"));
            var store = new JsonSettingsStore(paths.SettingsFolder);

            var (settings, workspace) = AppStart.Prepare(paths, store);

            Assert.Equal(paths.DefaultWorkspace, settings.WorkspacePath);
            Assert.True(Directory.Exists(workspace.Projects));
            Assert.True(Directory.Exists(workspace.Builds));
            Assert.True(File.Exists(Path.Combine(paths.SettingsFolder, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_stored_workspace_is_honoured_on_the_next_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-start-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "appdata"), Path.Combine(root, "docs", "SCMW"));
            var store = new JsonSettingsStore(paths.SettingsFolder);
            var moved = Path.Combine(root, "elsewhere");

            store.Save(new AppSettings(moved, @"D:\Games\SCUM"));

            var (settings, workspace) = AppStart.Prepare(paths, store);

            Assert.Equal(moved, settings.WorkspacePath);
            Assert.Equal(moved, workspace.Root);
            Assert.Equal(@"D:\Games\SCUM", settings.GamePath);   // not clobbered
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
