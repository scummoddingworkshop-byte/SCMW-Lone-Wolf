using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class BuildProjectChangesTests
{
    [AvaloniaFact]
    public void A_change_survives_a_restart_for_build()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var writer = TestProjects.ChangeStore(projects);
            writer.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);

            // A brand-new store and Build screen over the same workspace is the "restart".
            var restarted = new BuildViewModel(TestProjects.ChangeStore(projects), projects: projects);

            var line = Assert.IsType<BuildChange>(restarted.Changes[0]);
            Assert.Equal("Duct Tape", line.ItemName);
            var value = Assert.Single(line.Values);
            Assert.Equal("Max Stack", value.ValueName);   // shown friendly
            Assert.Equal("10 → 25", value.Summary);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Editing_the_same_value_twice_shows_one_up_to_date_row()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "30", projects.Current!.Id);
            var vm = new BuildViewModel(changes, projects: projects);

            var line = Assert.Single(vm.Changes);
            Assert.Equal("10 → 30", Assert.Single(line.Values).Summary);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Switching_projects_switches_the_build_list()
    {
        var (projects, root) = TestProjects.With("Project A", "Project B");
        try
        {
            var a = projects.All().Single(p => p.Name == "Project A");
            var b = projects.All().Single(p => p.Name == "Project B");
            projects.SetCurrent(a.Id);

            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", a.Id);
            var vm = new BuildViewModel(changes, projects: projects);
            Assert.Single(vm.Changes);

            projects.SetCurrent(b.Id);
            Assert.Empty(vm.Changes);
            Assert.True(vm.ShowNudge);
            Assert.False(vm.CanBuild);

            changes.RecordEdit("AK-15", "Damage", "45", "60", b.Id);
            var line = Assert.Single(vm.Changes);
            Assert.Equal("AK-15", line.ItemName);
        }
        finally { TestProjects.Cleanup(root); }
    }
}
