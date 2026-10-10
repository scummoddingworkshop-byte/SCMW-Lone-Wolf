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
    public void A_successful_build_installs_the_mod()
    {
        var (build, mods) = NewBuild();
        build.ProjectName = "Tape mod";

        build.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) build.Advance();

        var mod = Assert.Single(mods.Mods);
        Assert.Equal("Tape mod", mod.Name);
        Assert.False(mod.IsExternal);
        Assert.True(mod.IsOn);
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

    [AvaloniaFact]
    public void The_shell_end_to_end_build_lands_in_mods()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: projects);

            // Change a value, build it, then look in Mods.
            vm.Items.SelectedItem = vm.Items.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.Items.SelectedItem!.Values.First(v => v.Name == "Max stack");
            vm.Items.BeginEditCommand.Execute(stack);
            stack.Draft = "25";
            vm.Items.CommitEditCommand.Execute(stack);

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
