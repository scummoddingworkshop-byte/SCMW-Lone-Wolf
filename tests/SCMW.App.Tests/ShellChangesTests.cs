using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ShellChangesTests
{
    [AvaloniaFact]
    public void An_edit_on_items_appears_in_build_after_a_restart()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            // The first run of the app: change a value on Items.
            var first = new ShellViewModel(new NavigationHost(), projects: projects);
            first.Items.SelectedItem = first.Items.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = first.Items.SelectedItem!.Values.First(v => v.Name == "Max stack");
            first.Items.BeginEditCommand.Execute(stack);
            stack.Draft = "25";
            first.Items.CommitEditCommand.Execute(stack);

            Assert.True(first.Build.HasChanges);
            Assert.Contains(first.Build.Changes, c => c.ItemName == "Duct Tape" && c.Values.Any(v => v.Summary == "10 → 25"));

            // Close and reopen: a brand-new shell over the same workspace.
            var restarted = new ShellViewModel(new NavigationHost(), projects: projects);

            Assert.Contains(restarted.Build.Changes, c => c.ItemName == "Duct Tape" && c.Values.Any(v => v.Summary == "10 → 25"));
            restarted.Items.SelectedItem = restarted.Items.VisibleItems.First(i => i.Name == "Duct Tape");
            var restartedStack = restarted.Items.SelectedItem!.Values.First(v => v.Name == "Max stack");
            Assert.Equal("25", restartedStack.Value);
            Assert.True(restartedStack.IsChanged);
        }
        finally { TestProjects.Cleanup(root); }
    }
}
