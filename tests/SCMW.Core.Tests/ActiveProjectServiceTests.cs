using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

// Task 1 (Plan 3) Core tests: the ActiveProjectService helper that persists the open project's
// id in settings and resolves it back safely. No UI involvement.
public class ActiveProjectServiceTests
{
    private static WorkspacePaths TempWs()
    {
        var root = Path.Combine(Path.GetTempPath(), "scmw-active-" + Guid.NewGuid());
        return WorkspaceService.Ensure(root);
    }

    [Fact]
    public void Set_persists_the_id_and_Resolve_returns_the_project()
    {
        var ws = TempWs();
        var svc = new ProjectService(ws);
        var project = svc.Create("Open Me").Project!;
        var store = new FakeSettingsStore();
        var active = new ActiveProjectService(store, svc);

        Assert.Null(active.Resolve());               // nothing open yet

        active.Set(project.Id);

        Assert.Equal(project.Id, store.Load().ActiveProjectId); // persisted
        Assert.Equal(project.Id, active.StoredId);
        Assert.Equal(project.Id, active.Resolve()!.Id);         // resolves to the real project
    }

    [Fact] // a stale id must be cleared safely, never throw
    public void Resolve_clears_an_unknown_id_and_returns_null()
    {
        var ws = TempWs();
        var store = new FakeSettingsStore { Current = new AppSettings(null, null, ActiveProjectId: "ghost123") };
        var active = new ActiveProjectService(store, new ProjectService(ws));

        var resolved = Record.Exception(() => active.Resolve());

        Assert.Null(resolved);
        Assert.Null(active.Resolve());
        Assert.Null(store.Load().ActiveProjectId);   // cleared from settings
    }

    [Fact]
    public void TryActivate_unknown_id_clears_and_returns_null()
    {
        var ws = TempWs();
        var store = new FakeSettingsStore { Current = new AppSettings(null, null, ActiveProjectId: "ghost123") };
        var active = new ActiveProjectService(store, new ProjectService(ws));

        Assert.Null(active.TryActivate("otherGhost"));
        Assert.Null(store.Load().ActiveProjectId);
    }

    [Fact]
    public void TryActivate_real_id_persists_and_returns_the_project()
    {
        var ws = TempWs();
        var svc = new ProjectService(ws);
        var project = svc.Create("Activate Me").Project!;
        var store = new FakeSettingsStore();
        var active = new ActiveProjectService(store, svc);

        var activated = active.TryActivate(project.Id);

        Assert.Equal(project.Id, activated!.Id);
        Assert.Equal(project.Id, store.Load().ActiveProjectId);
    }

    [Fact]
    public void Clear_forgets_the_active_id()
    {
        var ws = TempWs();
        var svc = new ProjectService(ws);
        var project = svc.Create("Clear Me").Project!;
        var store = new FakeSettingsStore();
        var active = new ActiveProjectService(store, svc);
        active.Set(project.Id);

        active.Clear();

        Assert.Null(store.Load().ActiveProjectId);
        Assert.Null(active.Resolve());
    }

    [Fact] // no store and no lookup are both "no active project", silently
    public void Missing_store_or_service_resolves_to_null()
    {
        var noStore = new ActiveProjectService(null, new ProjectService(TempWs()));
        Assert.Null(noStore.Resolve());
        noStore.Set("anything");                      // must not throw
        noStore.Clear();

        var store = new FakeSettingsStore { Current = new AppSettings(null, null, ActiveProjectId: "abc") };
        var noLookup = new ActiveProjectService(store, projects: null);
        Assert.Null(noLookup.Resolve());
        // With no lookup we cannot verify, so the id is left intact rather than guessed away.
        Assert.Equal("abc", store.Load().ActiveProjectId);
    }

    /// <summary>Minimal in-memory settings store so the helper tests never touch disk.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Current { get; set; } = new(null, null);

        public AppSettings Load() => Current;

        public void Save(AppSettings s) => Current = s;
    }
}
