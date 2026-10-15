using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace SCMW.App.Tests;

public class BuildInstallsTests : IDisposable
{
    private readonly List<string> _roots = new();

    public void Dispose()
    {
        foreach (var root in _roots) TestProjects.Cleanup(root);
    }

    private (BuildViewModel build, ModList mods) NewBuild()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
        var mods = new ModList();
        return (new BuildViewModel(changes, mods: mods, projects: projects), mods);
    }

    [AvaloniaFact]
    public void A_successful_build_lands_in_mods_switched_off()
    {
        var (build, mods) = NewBuild();
        build.ProjectName = "Tape mod";

        build.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) build.Advance();

        var mod = Assert.Single(mods.Mods);
        Assert.Equal("Tape mod", mod.Name);
        Assert.False(mod.IsExternal);
        Assert.False(mod.IsOn);          // the player switches it on in Mods Management
    }

    [AvaloniaFact]
    public void Building_the_same_project_again_does_not_duplicate_it()
    {
        var (build, mods) = NewBuild();
        build.ProjectName = "Tape mod";

        build.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) build.Advance();

        build.ProjectName = "Tape mod";
        build.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) build.Advance();

        Assert.Single(mods.Mods);
    }

    [AvaloniaFact]
    public void An_unfinished_build_installs_nothing()
    {
        var (build, mods) = NewBuild();

        build.BuildCommand.Execute(null);
        build.Advance();

        Assert.Empty(mods.Mods);
    }

    [AvaloniaFact] // design §11: keep a copy of the saved game before installing a mod
    public void An_install_backs_up_the_saved_game_first()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);

        var saves = new RecordingSaves();
        var build = new BuildViewModel(changes, mods: new ModList(), projects: projects, saves: saves);

        build.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) build.Advance();

        Assert.True(saves.BackedUp);
    }

    [AvaloniaFact] // "one project = one mod": Build won't build a project the player did not choose
    public void Build_needs_a_chosen_project()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
        projects.SetCurrent("");   // a project with a change exists, but none is chosen

        var build = new BuildViewModel(changes, mods: new ModList(), projects: projects);

        Assert.True(build.NeedsProject);
        Assert.Empty(build.Changes);
        Assert.False(build.CanBuild);
    }

    /// <summary>A stand-in save service that only records whether a backup was taken.</summary>
    private sealed class RecordingSaves : ISaveBackupsService
    {
        public bool BackedUp { get; private set; }
        public bool IsAvailable => true;
        public string SaveFolder => "";
        public IReadOnlyList<SaveSnapshot> List() => Array.Empty<SaveSnapshot>();
        public string? BackUp() { BackedUp = true; return null; }
        public string? Restore(string snapshotId) => null;
        public string? Delete(string snapshotId) => null;
    }

    [AvaloniaFact]
    public void The_shell_end_to_end_build_lands_in_mods()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: projects);

            // Change a value, build it, then look in Mods.
            vm.ItemChanges.RecordEdit("Duct Tape", "Max stack", "10", "25", vm.ProjectStore.Current!.Id);

            vm.Build.BuildCommand.Execute(null);
            for (var i = 0; i < 5; i++) vm.Build.Advance();

            vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);

            var window = new MainWindow { DataContext = vm };
            window.Show();

            Assert.IsType<SCMW.App.Views.ModsView>(vm.ActiveView);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("My first mod", texts);
        }
        finally { TestProjects.Cleanup(root); }
    }
}
