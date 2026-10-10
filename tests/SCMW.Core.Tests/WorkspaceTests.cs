using SCMW.Core.Workspace;
using Xunit;

public class WorkspaceTests
{
    [Fact]
    public void Ensure_creates_all_dirs_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-" + Guid.NewGuid());
        var p1 = WorkspaceService.Ensure(root);
        var p2 = WorkspaceService.Ensure(root);
        Assert.All(new[]{p1.Projects, p1.Builds, p1.Backups, p1.Knowledge, p1.Logs, p1.Cache},
                   d => Assert.True(Directory.Exists(d)));
        Assert.Equal(p1.Projects, p2.Projects);
    }

    [Fact] // Review Focus #2: spaces + unicode survive round-trip
    public void Workspace_path_with_spaces_and_unicode_works()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw mit äöü 中文");
        var p = WorkspaceService.Ensure(root);
        Assert.True(Directory.Exists(p.Projects));
    }
}
