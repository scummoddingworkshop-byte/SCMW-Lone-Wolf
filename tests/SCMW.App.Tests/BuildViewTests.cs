using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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

    /// <summary>A project with plenty of changed items, to see what a long list does to the layout.</summary>
    private BuildViewModel ManyChanges(int count)
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        for (var i = 0; i < count; i++)
            changes.RecordEdit($"Item {i:00}", "Max stack", "10", "25", projects.Current!.Id);
        return new BuildViewModel(changes, projects: projects);
    }

    [AvaloniaFact]
    public void Build_shows_the_project_and_the_nudge_when_empty()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var vm = new BuildViewModel(TestProjects.ChangeStore(projects), projects: projects);
        var (view, _) = ShownView(vm);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("My first mod", texts);          // the preview names the project
        Assert.True(view.FindControl<Border>("Nudge")!.IsVisible);
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
    public void The_preview_area_is_titled_whats_in_this_mod()
    {
        var (view, _) = ShownView();

        var title = view.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("h1"));
        Assert.Equal("What's in this mod", title.Text);
    }

    [AvaloniaFact]
    public void The_changed_items_list_is_never_cut_off()
    {
        var (view, _) = ShownView(ManyChanges(20));

        var preview = view.FindControl<PreviewView>("PreviewArea");
        Assert.NotNull(preview);

        // The cards are laid out on the page itself — no inner scroller slicing the last row.
        var list = preview!.FindControl<ItemsControl>("ChangeList");
        Assert.NotNull(list);
        Assert.Empty(list!.GetVisualAncestors().OfType<ScrollViewer>()
                         .Where(s => s.Name == "PreviewScroll"));

        // With plenty of rows the list grows to hold them, so nothing is clipped.
        Assert.True(list.Bounds.Height > 400,
            $"the list only measured {list.Bounds.Height} — the cards would be cut off");
    }

    [AvaloniaFact]
    public void A_version_that_was_built_before_is_asked_about_first()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);

        // That exact version is already in the mods list, so it has been built before.
        var mods = new ModList();
        mods.Install(SCMW.Backend.Build.BuildNames.PakFileName("My first mod", "1.0.0"));

        var vm = new BuildViewModel(changes, projects: projects, mods: mods);
        Assert.True(vm.CanBuild);

        vm.BuildCommand.Execute(null);

        Assert.True(vm.HasDuplicateVersionNotice);          // asked, not built
        Assert.Contains("already been built", vm.DuplicateVersionNotice);
        Assert.NotEqual(BuildState.Running, vm.State);

        // Changing the version answers the question.
        vm.ModVersion = "1.1.0";
        Assert.False(vm.HasDuplicateVersionNotice);
    }

    [AvaloniaFact]
    public void An_empty_version_builds_as_1_0_0()
    {
        var vm = new BuildViewModel();
        vm.ModVersion = "   ";

        Assert.Equal("1.0.0", vm.EffectiveVersion);

        vm.ModVersion = "My first release";
        Assert.Equal("My first release", vm.EffectiveVersion);   // any words the player likes
    }

    [AvaloniaFact]
    public void Clicking_a_build_card_asks_to_open_that_item()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id,
                           assetPath: "/a/Duct_Tape_ES");

        var vm = new BuildViewModel(changes, projects: projects);
        string? asked = null;
        vm.Preview.OpenItemRequested += path => asked = path;

        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();
        window.UpdateLayout();

        var card = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("card") && b.DataContext is BuildChange);

        var point = card.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal("/a/Duct_Tape_ES", asked);
    }

    [AvaloniaFact]
    public void A_build_card_lists_the_item_and_its_changed_values()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        var id = projects.Current!.Id;
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", id, assetPath: "/a/Duct_Tape_ES");
        changes.RecordEdit("Duct Tape", "Weight", "5", "3", id, assetPath: "/a/Duct_Tape_ES");

        var (view, _) = ShownView(new BuildViewModel(changes, projects: projects));
        Dispatcher.UIThread.RunJobs();   // let the shared-size columns settle

        var texts = view.GetVisualDescendants().OfType<TextBlock>().ToList();
        var name = texts.First(t => t.Text == "Duct Tape");
        var tag1 = texts.First(t => t.Text == FieldLabels.Of("Max stack"));
        var tag2 = texts.First(t => t.Text == FieldLabels.Of("Weight"));
        var value1 = texts.First(t => t.Text == "10 → 25");
        var value2 = texts.First(t => t.Text == "5 → 3");

        // The tags share one column and the values share another, so both line up down the card.
        Assert.Equal(tag1.Bounds.X, tag2.Bounds.X);
        Assert.Equal(value1.Bounds.X, value2.Bounds.X);

        // The changed values sit under the item's name, inside the same card.
        var card = tag1.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("card"));
        var atName = name.TranslatePoint(new Point(0, 0), card)!.Value;
        var atTag = tag1.TranslatePoint(new Point(0, 0), card)!.Value;

        Assert.True(atTag.Y > atName.Y,
            $"the values sat at {atTag.Y} but the name at {atName.Y} — they should be below it");
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
            vm.ItemChanges.RecordEdit("Duct Tape", "Max stack", "10", "25", vm.ProjectStore.Current!.Id);

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
    public async Task The_real_build_sends_you_to_mods_management()
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

            // After a real build succeeds, the only button offered is the way to Mods Management —
            // installing, switching off and removing all live there.
            await vm.BuildCommand.ExecuteAsync(null);

            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Contains(buttons, b => (b.Content as string) == "Mods Management" && b.IsVisible);
            Assert.DoesNotContain(buttons, b => (b.Content as string) == "Install into SCUM");
            Assert.DoesNotContain(buttons, b => (b.Content as string) == "Launch SCUM");

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Your mod is built and ready to be enabled in the Mods Management screen.", texts);
        }
        finally { TestProjects.Cleanup(root); }
    }

    private sealed class AlwaysOkBuildService : IBuildService
    {
        public Task<BuildOutcome> BuildAsync(string n, string v, IReadOnlyList<BuildItem> i,
            IReadOnlyList<NewBuildAsset> na, IReadOnlyList<NewBuildSection> sections,
            IProgress<int>? p, CancellationToken ct)
        { p?.Report(4); return Task.FromResult(new BuildOutcome(true, @"C:\ws\M_v1.0.0_P.pak", null)); }
        public Task<InstallOutcome> InstallAsync(string id, string n, string v, string pak,
                                                 bool enabled, CancellationToken ct)
            => Task.FromResult(new InstallOutcome(true, @"C:\g\~mods\M_v1.0.0_P.pak", null));
        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct) => Task.FromResult(new RemoveOutcome(true, null));
        public bool IsInstalled(string id) => false;
    }
}
