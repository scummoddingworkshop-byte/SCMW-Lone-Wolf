using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;
using SCMW.Backend.Build;

namespace SCMW.App.Tests;

public class BuildViewTests : IDisposable
{
    private readonly List<string> _roots = new();

    public void Dispose()
    {
        foreach (var root in _roots) TestProjects.Cleanup(root);
    }

    private static (BuildView view, BuildViewModel vm) ShownView(BuildViewModel? vm = null)
    {
        vm ??= new BuildViewModel();
        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();
        return (view, vm);
    }

    private BuildViewModel Buildable()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
        return new BuildViewModel(changes, projects: projects);
    }

    [AvaloniaFact]
    public void Build_shows_the_project_and_the_nudge_when_empty()
    {
        var (view, _) = ShownView();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("My first mod", texts);
        Assert.Contains("WHAT'S IN THIS MOD", texts);
        Assert.True(view.FindControl<Border>("Nudge")!.IsVisible);
        Assert.True(view.FindControl<Expander>("Advanced")!.IsVisible);
        Assert.False(view.FindControl<Button>("BuildButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Build_lists_the_changes_and_enables_the_button()
    {
        var (view, _) = ShownView(Buildable());

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Duct Tape", texts);
        Assert.Contains("10 → 25", texts);
        Assert.False(view.FindControl<Border>("Nudge")!.IsVisible);
        Assert.True(view.FindControl<Button>("BuildButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Section_headings_are_shown_in_capitals()
    {
        var (view, _) = ShownView();

        var heading = view.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("section"));
        Assert.Equal("WHAT'S IN THIS MOD", heading.Text);
    }

    [AvaloniaFact]
    public void Build_shows_the_step_bar_while_running()
    {
        var vm = Buildable();
        vm.BuildCommand.Execute(null);          // start before showing, so the pills are laid out

        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();

        Assert.True(view.FindControl<StackPanel>("StepBar")!.IsVisible);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Prepare", texts);
    }

    [AvaloniaFact]
    public void The_restart_as_administrator_button_follows_the_flag()
    {
        var (view, vm) = ShownView();

        var button = view.FindControl<Button>("RestartAdminButton");
        Assert.NotNull(button);
        Assert.False(button!.IsVisible);

        vm.CanRestartAsAdmin = true;

        Assert.True(button.IsVisible);
        Assert.Equal("Restart as administrator", button.Content);
    }

    [AvaloniaFact]
    public void Build_shows_success_and_failure_panels()
    {
        var (view, vm) = ShownView(Buildable());

        vm.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) vm.Advance();
        Assert.True(view.FindControl<Border>("SuccessPanel")!.IsVisible);

        vm.ShowFailure("Nope", "Because", "Try again", "details");
        Assert.True(view.FindControl<Border>("FailurePanel")!.IsVisible);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Try again", texts);
    }

    [AvaloniaFact]
    public void Build_area_shows_in_the_shell_and_sees_items_changes()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: projects);

            // Change a value on Items...
            vm.Items.SelectedItem = vm.Items.VisibleItems.First(i => i.Name == "Duct Tape");
            var stack = vm.Items.SelectedItem!.Values.First(v => v.Name == "Max stack");
            vm.Items.BeginEditCommand.Execute(stack);
            stack.Draft = "25";
            vm.Items.CommitEditCommand.Execute(stack);

            // ...then open Build and find it listed.
            vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Build);

            var window = new MainWindow { DataContext = vm };
            window.Show();

            Assert.IsType<BuildView>(vm.ActiveView);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Duct Tape", texts);
            Assert.Contains("10 → 25", texts);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_real_build_offers_install_and_remove()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape ES", "MaxStackSize", "10", "25", projects.Current!.Id);
            var vm = new BuildViewModel(changes, projects: projects, service: new AlwaysOkBuildService());
            var view = new BuildView { DataContext = vm };
            var window = new Window { Content = view, Width = 1280, Height = 900 };
            window.Show();

            // After a real build succeeds, an Install button exists and is visible.
            await vm.BuildCommand.ExecuteAsync(null);

            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Contains(buttons, b => (b.Content as string) == "Install into SCUM" && b.IsVisible);

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Your mod is built and ready to install.", texts);
        }
        finally { TestProjects.Cleanup(root); }
    }

    private sealed class AlwaysOkBuildService : IBuildService
    {
        public Task<BuildOutcome> BuildAsync(string n, string v, IReadOnlyList<BuildItem> i, IProgress<int>? p, CancellationToken ct)
        { p?.Report(4); return Task.FromResult(new BuildOutcome(true, @"C:\ws\M_v1.0.0_P.pak", null)); }
        public Task<InstallOutcome> InstallAsync(string id, string n, string v, string pak, CancellationToken ct)
            => Task.FromResult(new InstallOutcome(true, @"C:\g\~mods\M_v1.0.0_P.pak", null));
        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct) => Task.FromResult(new RemoveOutcome(true, null));
        public bool IsInstalled(string id) => false;
    }
}
