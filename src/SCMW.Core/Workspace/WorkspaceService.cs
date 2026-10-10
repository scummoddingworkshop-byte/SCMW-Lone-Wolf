namespace SCMW.Core.Workspace;

public sealed record WorkspacePaths(string Root, string Projects, string Builds, string Backups,
                                   string Knowledge, string Logs, string Cache);

public static class WorkspaceService
{
    public static WorkspacePaths Ensure(string root)
    {
        Directory.CreateDirectory(root); // idempotent
        var paths = new WorkspacePaths(
            Root: root,
            Projects: Path.Combine(root, "Projects"),
            Builds: Path.Combine(root, "Builds"),
            Backups: Path.Combine(root, "Backups"),
            Knowledge: Path.Combine(root, "Knowledge"),
            Logs: Path.Combine(root, "Logs"),
            Cache: Path.Combine(root, "Cache"));
        Directory.CreateDirectory(paths.Projects);
        Directory.CreateDirectory(paths.Builds);
        Directory.CreateDirectory(paths.Backups);
        Directory.CreateDirectory(paths.Knowledge);
        Directory.CreateDirectory(paths.Logs);
        Directory.CreateDirectory(paths.Cache);
        return paths;
    }
}
