using Avalonia.Headless.XUnit;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

public class ItemsChangeTests
{
    private static (ItemChangeStore Store, ProjectStore Projects, string Root) Workspace(params string[] names)
    {
        var (projects, root) = TestProjects.With(names);
        return (new ItemChangeStore(new ProjectChangeStore(), projects), projects, root);
    }

    private static void Edit(ItemsViewModel vm, string itemName, string valueName, string draft)
    {
        vm.SelectedItem = vm.VisibleItems.First(i => i.Name == itemName);
        var value = vm.SelectedItem!.Values.First(v => v.Name == valueName);
        vm.BeginEditCommand.Execute(value);
        value.Draft = draft;
        vm.CommitEditCommand.Execute(value);
    }

    [AvaloniaFact]
    public void Committing_a_value_writes_a_record_in_the_current_project()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            var vm = new ItemsViewModel(store, projects);

            Edit(vm, "Duct Tape", "Max stack", "25");

            var record = Assert.Single(store.For(projects.Current!.FolderPath));
            Assert.Equal("Duct Tape", record.AssetId);
            Assert.Equal("Max stack", record.FieldTechnicalName);
            Assert.Equal("10", record.OldValue);
            Assert.Equal("25", record.NewValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // "one project = one mod": even the old screen never quietly saves to a fall-back project
    public void Editing_with_no_chosen_project_is_refused()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            projects.SetCurrent("");   // projects exist, but none is chosen
            var vm = new ItemsViewModel(store, projects);

            Edit(vm, "Duct Tape", "Max stack", "25");

            Assert.Empty(store.For(projects.Current!.FolderPath));   // nothing recorded
            Assert.False(string.IsNullOrWhiteSpace(vm.RefusalMessage));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_fresh_screen_shows_the_edited_value_and_marks_it_changed()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            var first = new ItemsViewModel(store, projects);
            Edit(first, "Duct Tape", "Max stack", "25");

            // A brand-new screen over the same workspace is the "restart".
            var fresh = new ItemsViewModel(new ItemChangeStore(new ProjectChangeStore(), projects), projects);
            fresh.SelectedItem = fresh.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = fresh.SelectedItem!.Values.First(v => v.Name == "Max stack");

            Assert.Equal("25", stack.Value);
            Assert.True(stack.IsChanged);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Editing_in_one_project_leaves_another_untouched()
    {
        var (store, projects, root) = Workspace("Project A", "Project B");
        try
        {
            var a = projects.All().Single(p => p.Name == "Project A");
            var b = projects.All().Single(p => p.Name == "Project B");
            projects.SetCurrent(a.Id);

            var vm = new ItemsViewModel(store, projects);
            Edit(vm, "Duct Tape", "Max stack", "25");

            projects.SetCurrent(b.Id);

            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.SelectedItem!.Values.First(v => v.Name == "Max stack");
            Assert.Equal("10", stack.Value);
            Assert.False(stack.IsChanged);
            Assert.Empty(store.For(b.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Cancelling_writes_nothing()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            var vm = new ItemsViewModel(store, projects);
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.SelectedItem!.Values.First(v => v.Name == "Max stack");

            vm.BeginEditCommand.Execute(stack);
            stack.Draft = "99";
            vm.CancelEditCommand.Execute(stack);

            Assert.Empty(store.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_original_shown_is_the_first_records_old_value()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            store.RecordEdit("Duct Tape", "Max stack", "7", "25", projects.Current!.Id);

            var vm = new ItemsViewModel(store, projects);
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.SelectedItem!.Values.First(v => v.Name == "Max stack");

            Assert.Equal("25", stack.Value);
            Assert.Equal("7", stack.OriginalValue);
            Assert.Equal("was 7", stack.WasText);
            Assert.True(stack.IsChanged);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Committing_the_unchanged_value_writes_nothing()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            var vm = new ItemsViewModel(store, projects);
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.SelectedItem!.Values.First(v => v.Name == "Max stack");

            vm.BeginEditCommand.Execute(stack);
            stack.Draft = "10";           // the same as the game value
            vm.CommitEditCommand.Execute(stack);

            Assert.Empty(store.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Editing_a_value_back_to_the_original_writes_nothing_new()
    {
        var (store, projects, root) = Workspace("My first mod");
        try
        {
            var vm = new ItemsViewModel(store, projects);
            Edit(vm, "Duct Tape", "Max stack", "25");

            var rewritten = new ItemsViewModel(new ItemChangeStore(new ProjectChangeStore(), projects), projects);
            Edit(rewritten, "Duct Tape", "Max stack", "10");   // back to the original

            // Only the first edit is recorded; the no-op second edit adds nothing.
            Assert.Single(store.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void With_no_current_project_editing_is_refused_politely()
    {
        var (store, projects, root) = Workspace();
        try
        {
            var vm = new ItemsViewModel(store, projects);
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.SelectedItem!.Values.First(v => v.Name == "Max stack");

            vm.BeginEditCommand.Execute(stack);
            stack.Draft = "25";
            vm.CommitEditCommand.Execute(stack);

            Assert.Equal("10", stack.Value);
            Assert.NotEqual("", vm.RefusalMessage);
        }
        finally { TestProjects.Cleanup(root); }
    }
}
