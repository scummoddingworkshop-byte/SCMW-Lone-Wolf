using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SCMW.App.Setup;
using SCMW.Core.Logging;
using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The one place that touches the real disk: prepare the workspace and settings.
            var paths = AppPaths.Default;
            ISettingsStore settingsStore;
            WorkspacePaths? workspace = null;
            try
            {
                settingsStore = new JsonSettingsStore(paths.SettingsFolder);
                (_, workspace) = AppStart.Prepare(paths, settingsStore);
            }
            catch
            {
                // The workspace could not be prepared (a locked or unusable path). Open
                // anyway, in memory, and the Settings screen says so plainly.
                settingsStore = new InMemorySettingsStore();
            }

            // Switch the log file on as early as we can, so a startup problem is written down.
            if (workspace is not null)
            {
                Log.SetLogsDir(workspace.Logs);
                Log.Info($"{AppInfo.Name} {AppInfo.Version} starting");
            }

            var projectStore = workspace is null
                ? ProjectStore.Default()
                : new ProjectStore(new ProjectService(workspace), settingsStore);

            var window = new MainWindow
            {
                DataContext = new ShellViewModel(
                    settingsStore: settingsStore,
                    scumSources: WindowsScumSources.Default,
                    projects: projectStore),
            };

            // Reopen at the size and place the player last left it.
            WindowPlacement.Apply(window, settingsStore.Load());
            window.Closing += (_, _) => WindowPlacement.Save(window, settingsStore);

            // Let "Restart as administrator" close this window cleanly (so its place is remembered).
            WindowsElevation.Default.Shutdown = () => desktop.Shutdown();

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}