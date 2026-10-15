using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    public void The_changed_items_list_scrolls_so_the_build_button_stays_in_view()
    {
        var (view, _) = ShownView(ManyChanges(20));

        var preview = view.FindControl<PreviewView>("PreviewArea");
        Assert.NotNull(preview);

        var scroll = preview!.FindControl<ScrollViewer>("PreviewScroll");
        Assert.NotNull(scroll);
        Assert.True(scroll!.MaxHeight < double.PositiveInfinity,
            "the changed-items list must be capped, or a long list pushes Build off the screen");
        Assert.True(scroll.MaxHeight <= 600);

        // The change list really lives inside that scroller...
        var list = preview.FindControl<ItemsControl>("ChangeList");
        Assert.NotNull(list);
        Assert.Contains(scroll, list!.GetVisualAncestors().OfType<ScrollViewer>());

        // ...and with plenty of rows it is capped, so it can't push the button down.
        Assert.True(scroll.Bounds.Height <= scroll.MaxHeight + 1,
            $"the list grew to {scroll.Bounds.Height}, past its {scroll.MaxHeight} cap");
    }

    [AvaloniaFact]
    public void The_field_tags_line_up_in_the_right_third_beside_the_name()
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
        var tag1 = texts.First(t => t.Text == "Max Stack");
        var tag2 = texts.First(t => t.Text == "Weight");
        var value1 = texts.First(t => t.Text == "10 → 25");
        var value2 = texts.First(t => t.Text == "5 → 3");

        // The tags share one column and the values share another, so both line up down the card.
        Assert.Equal(tag1.Bounds.X, tag2.Bounds.X);
        Assert.Equal(value1.Bounds.X, value2.Bounds.X);

        var card = tag1.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("card"));
        var fields = tag1.GetVisualAncestors().OfType<ItemsControl>().First();
        var atFields = fields.TranslatePoint(new Point(0, 0), card)!.Value;
        var atName = name.TranslatePoint(new Point(0, 0), card)!.Value;

        // The changed fields sit in the right third, beside the item's name...
        Assert.True(atFields.X > card.Bounds.Width / 2,
            $"the fields sat at {atFields.X} of {card.Bounds.Width} — they should be in the right third");

        // ...and share a middle line with the icon and name.
        var fieldsMiddle = atFields.Y + fields.Bounds.Height / 2;
        var nameMiddle = atName.Y + name.Bounds.Height / 2;
        Assert.True(Math.Abs(fieldsMiddle - nameMiddle) < 2,
            $"the fields' middle was {fieldsMiddle} but the name's was {nameMiddle} — they should match");
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
