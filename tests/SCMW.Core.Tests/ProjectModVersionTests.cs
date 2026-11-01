using SCMW.Core.Projects;
using SCMW.Core.Workspace;

/// <summary>
/// The mod version belongs to the project, so every project card can show its own and keep it
/// between sessions (Build's box used to forget it on every restart).
/// </summary>
public class ProjectModVersionTests
{
    private static ProjectService Service()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-modversion-" + Guid.NewGuid().ToString("N"));
        return new ProjectService(WorkspaceService.Ensure(root));
    }

    [Fact]
    public void A_new_project_starts_at_version_1_0_0()
    {
        var svc = Service();

        var project = svc.Create("Tape mod").Project!;

        Assert.Equal("1.0.0", project.ModVersion);
    }

    [Fact]
    public void A_project_remembers_the_version_it_was_given()
    {
        var svc = Service();
        var project = svc.Create("Tape mod").Project!;

        Assert.Null(svc.SetVersion(project.Id, "2.4.1"));

        Assert.Equal("2.4.1", svc.GetById(project.Id)!.ModVersion);
    }

    [Fact] // an old project file has no version field: it must still open, at 1.0.0
    public void An_old_project_file_reads_as_version_1_0_0()
    {
        var svc = Service();
        var project = svc.Create("Tape mod").Project!;
        File.WriteAllText(Path.Combine(project.FolderPath, ProjectService.ProjectFileName),
            "{ \"id\": \"" + project.Id + "\", \"name\": \"Tape mod\", \"description\": \"\", " +
            "\"createdUtc\": \"2026-10-01T10:00:00Z\", \"modifiedUtc\": \"2026-10-01T10:00:00Z\" }");

        Assert.Equal("1.0.0", svc.GetById(project.Id)!.ModVersion);
    }

    [Fact] // renaming or editing a project must not lose its version
    public void Editing_a_project_keeps_its_version()
    {
        var svc = Service();
        var project = svc.Create("Tape mod").Project!;
        svc.SetVersion(project.Id, "3.1.0");

        Assert.Null(svc.Rename(project.Id, "Tape mod two"));

        Assert.Equal("3.1.0", svc.GetById(project.Id)!.ModVersion);
    }

    [Fact] // saving a version must not move the card in the list (it is not an edit of the mod)
    public void Setting_a_version_does_not_reorder_the_projects()
    {
        var svc = Service();
        var older = svc.Create("Older").Project!;
        Task.Delay(20).Wait();
        var newer = svc.Create("Newer").Project!;
        Assert.Equal(newer.Id, svc.List()[0].Id);

        svc.SetVersion(older.Id, "2.0.0");

        Assert.Equal(newer.Id, svc.List()[0].Id);
    }
}
