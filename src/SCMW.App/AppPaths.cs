using System;
using System.IO;

namespace SCMW.App;

/// <summary>The one place that decides where the app keeps its own things.</summary>
public sealed class AppPaths
{
    public AppPaths(string settingsFolder, string defaultWorkspace)
    {
        SettingsFolder = settingsFolder;
        DefaultWorkspace = defaultWorkspace;
    }

    /// <summary>Settings live outside the workspace, so moving the workspace cannot lose them.</summary>
    public string SettingsFolder { get; }

    public string DefaultWorkspace { get; }

    public static AppPaths Default { get; } = new(
        settingsFolder: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCMW"),
        defaultWorkspace: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SCMW"));
}
