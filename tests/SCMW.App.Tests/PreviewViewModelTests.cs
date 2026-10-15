using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class PreviewViewModelTests
{
    [AvaloniaFact] // Task 4.3: the diff lists only changed files, with before → after
    public void The_preview_lists_only_changed_files_with_before_and_after()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;

            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", id, assetPath: "/a/Duct_Tape");
            changes.RecordMultiply("Batteries", "Weight", "400", "2", id, assetPath: "/a/Batteries");

            var vm = new PreviewViewModel(changes, projects);

            Assert.Equal(2, vm.Changes.Count);   // only the two changed files, nothing untouched

            var duct = vm.Changes.Single(c => c.ItemName == "Duct Tape");
            Assert.Contains(duct.Values, v => v.Summary == "10 → 25");

            var batteries = vm.Changes.Single(c => c.ItemName == "Batteries");
            Assert.Contains(batteries.Values, v => v.Summary == "400 → 800");   // the ×2 is worked out
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // Task 5.2: an added field reads "(not set) → value" in the preview
    public void The_preview_shows_an_added_field_as_not_set_before()
    {
        var (projects, root) = TestProjects.With("Tomato mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;

            changes.RecordAddField("/a/Tomato_ES", "_autoDestructTime", "0", "/a/Stone_ES", id,
                                   assetPath: "/a/Tomato_ES");

            var vm = new PreviewViewModel(changes, projects);

            var tomato = Assert.Single(vm.Changes);
            Assert.Contains(tomato.Values, v => v.Summary == "(not set) → 0");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // polish: each change shows the item's friendly name and its full path
    public void The_preview_names_each_item_and_shows_its_path()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", id, assetPath: "/a/Duct_Tape_ES");

            var vm = new PreviewViewModel(changes, projects);

            var line = Assert.Single(vm.Changes);
            Assert.Equal("/a/Duct_Tape_ES", line.Path);
            Assert.Equal("Duct Tape", line.FriendlyName);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // with nothing chosen, the preview says so instead of showing a fall-back's changes
    public void The_preview_says_so_when_no_project_is_chosen()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            projects.SetCurrent("");
            var changes = TestProjects.ChangeStore(projects);

            var vm = new PreviewViewModel(changes, projects);

            Assert.True(vm.NeedsProject);
            Assert.Empty(vm.Changes);
            Assert.False(string.IsNullOrWhiteSpace(vm.ProjectWarning));
        }
        finally { TestProjects.Cleanup(root); }
    }
}
