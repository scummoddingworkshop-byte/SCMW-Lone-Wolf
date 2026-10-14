using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class BuildViewModelTests
{
    [AvaloniaFact]
    public void Starts_with_a_project_name_and_version()
    {
        var vm = new BuildViewModel();

        Assert.Equal("My first mod", vm.ProjectName);
        Assert.Equal("1.0.0", vm.ModVersion);
        Assert.Equal(BuildState.Idle, vm.State);
    }

    [AvaloniaFact]
    public void Renaming_waits_for_the_commit_not_every_keystroke()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new BuildViewModel(projects: projects);

            vm.RenameDraft = "Tape mod";
            Assert.Equal("My first mod", projects.Current!.Name);   // typing does not rename

            vm.CommitRenameCommand.Execute(null);
            Assert.Equal("Tape mod", projects.Current!.Name);       // committing does
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Lists_the_projects_saved_changes()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
            var vm = new BuildViewModel(changes, projects: projects);

            Assert.True(vm.HasChanges);
            Assert.False(vm.ShowNudge);
            var row = Assert.Single(vm.Changes);
            Assert.Equal("Duct Tape", row.ItemName);
            var value = Assert.Single(row.Values);
            Assert.Equal("Max Stack", value.ValueName);   // shown friendly
            Assert.Equal("10 → 25", value.Summary);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Two_changes_on_one_item_share_a_single_row()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
            changes.RecordEdit("Duct Tape", "Weight", "5", "3", projects.Current!.Id);
            changes.RecordEdit("AK-15", "Damage", "45", "60", projects.Current!.Id);
            var vm = new BuildViewModel(changes, projects: projects);

            Assert.Equal(2, vm.Changes.Count);                 // two items, not three rows
            var duct = vm.Changes.Single(c => c.ItemName == "Duct Tape");
            Assert.Equal(2, duct.Values.Count);
            Assert.Contains(duct.Values, v => v.ValueName == "Max Stack" && v.Summary == "10 → 25");
            Assert.Contains(duct.Values, v => v.ValueName == "Weight" && v.Summary == "5 → 3");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Removing_an_item_drops_all_of_its_changes()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
            changes.RecordEdit("Duct Tape", "Weight", "5", "3", projects.Current!.Id);
            changes.RecordEdit("AK-15", "Damage", "45", "60", projects.Current!.Id);
            var vm = new BuildViewModel(changes, projects: projects);

            var duct = vm.Changes.Single(c => c.ItemName == "Duct Tape");
            vm.RemoveItemCommand.Execute(duct);

            Assert.Single(vm.Changes);                          // the whole item went, not just one value
            Assert.Equal("AK-15", vm.Changes[0].ItemName);
            Assert.DoesNotContain(changes.For(projects.Current!.FolderPath), r => r.AssetId == "Duct Tape");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void With_nothing_to_build_it_shows_the_nudge_and_cannot_build()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new BuildViewModel(TestProjects.ChangeStore(projects), projects: projects);

            Assert.True(vm.ShowNudge);
            Assert.False(vm.CanBuild);
            Assert.False(vm.BuildCommand.CanExecute(null));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void With_a_change_it_can_build()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
            var vm = new BuildViewModel(changes, projects: projects);

            Assert.True(vm.CanBuild);
            Assert.True(vm.BuildCommand.CanExecute(null));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Go_to_items_navigates()
    {
        var nav = new NavigationHost();
        var vm = new BuildViewModel(null, nav);

        vm.GoToItemsCommand.Execute(null);

        Assert.Equal(ScreenKey.Workspace, nav.Current);
    }

    [AvaloniaFact]
    public void Renaming_and_version_are_kept_in_memory()
    {
        var vm = new BuildViewModel();

        vm.ProjectName = "Tape mod";
        vm.ModVersion = "2.1";

        Assert.Equal("Tape mod", vm.ProjectName);
        Assert.Equal("2.1", vm.ModVersion);
    }
}
