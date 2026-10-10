using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class BuildRunTests : IDisposable
{
    private readonly List<string> _roots = new();

    public void Dispose()
    {
        foreach (var root in _roots) TestProjects.Cleanup(root);
    }

    private (BuildViewModel vm, ItemChangeStore changes, ProjectStore projects) NewBuildable()
    {
        var (projects, root) = TestProjects.With("My first mod");
        _roots.Add(root);
        var changes = TestProjects.ChangeStore(projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
        return (new BuildViewModel(changes, projects: projects), changes, projects);
    }

    [AvaloniaFact]
    public void Pressing_build_starts_the_first_step()
    {
        var (vm, _, _) = NewBuildable();

        vm.BuildCommand.Execute(null);

        Assert.Equal(BuildState.Running, vm.State);
        Assert.True(vm.ShowStepBar);
        Assert.True(vm.Steps[0].IsActive);
        Assert.False(vm.CanBuild);            // cannot build again while running
    }

    [AvaloniaFact]
    public void The_steps_advance_and_end_in_success()
    {
        var (vm, _, _) = NewBuildable();
        vm.BuildCommand.Execute(null);

        vm.Advance();                          // Check
        Assert.True(vm.Steps[1].IsActive);
        Assert.True(vm.Steps[0].IsDone);

        vm.Advance();                          // Build
        vm.Advance();                          // Package
        vm.Advance();                          // Ready
        Assert.False(vm.IsSucceeded);          // still on the last step

        vm.Advance();                          // past the last step

        Assert.Equal(BuildState.Succeeded, vm.State);
        Assert.True(vm.IsSucceeded);
        Assert.All(vm.Steps, s => Assert.True(s.IsDone));
        Assert.False(vm.ShowStepBar);
    }

    [AvaloniaFact]
    public void The_status_text_follows_the_step()
    {
        var (vm, _, _) = NewBuildable();
        vm.BuildCommand.Execute(null);

        Assert.Equal("Preparing…", vm.StatusText);

        vm.Advance();
        Assert.Equal("Checking your changes…", vm.StatusText);

        vm.Advance();
        Assert.Equal("Making the mod…", vm.StatusText);
    }

    [AvaloniaFact]
    public void The_run_stops_advancing_once_it_is_finished()
    {
        var (vm, _, _) = NewBuildable();
        vm.BuildCommand.Execute(null);
        for (var i = 0; i < 6; i++) vm.Advance();

        vm.Advance();                          // one more, after success

        Assert.Equal(BuildState.Succeeded, vm.State);
    }

    [AvaloniaFact]
    public void Failure_is_shown_in_plain_words()
    {
        var (vm, _, _) = NewBuildable();

        vm.ShowFailure(
            "The mod could not be made.",
            "Something in the game files was not in the shape we expected.",
            "Check your SCUM folder in Settings, then try again.",
            "System.IO.IOException: unexpected end of stream");

        Assert.Equal(BuildState.Failed, vm.State);
        Assert.True(vm.IsFailed);
        Assert.Contains("could not be made", vm.FailureWhat);
        Assert.Contains("Settings", vm.FailureNext);
        Assert.Contains("IOException", vm.FailureDetails);
    }

    [AvaloniaFact]
    public void Recording_another_change_after_a_build_clears_the_outcome()
    {
        var (vm, changes, projects) = NewBuildable();
        vm.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) vm.Advance();
        Assert.True(vm.IsSucceeded);

        changes.RecordEdit("AK-15", "Damage", "45", "60", projects.Current!.Id);

        Assert.Equal(BuildState.Idle, vm.State);
        Assert.False(vm.IsSucceeded);
    }

    [AvaloniaFact]
    public void Switching_the_current_project_clears_the_outcome()
    {
        var (vm, _, projects) = NewBuildable();
        vm.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) vm.Advance();
        Assert.True(vm.IsSucceeded);

        var other = projects.Create("Another mod", "").Project!;
        projects.SetCurrent(other.Id);

        Assert.Equal(BuildState.Idle, vm.State);
        Assert.True(vm.ShowNudge);              // the other project has no changes
        Assert.False(vm.CanBuild);
    }

    [AvaloniaFact]
    public void Changing_the_list_during_a_build_cancels_it()
    {
        var (vm, changes, projects) = NewBuildable();
        vm.BuildCommand.Execute(null);
        Assert.True(vm.IsRunning);

        changes.RecordEdit("AK-15", "Damage", "45", "60", projects.Current!.Id);

        Assert.Equal(BuildState.Idle, vm.State);
        Assert.False(vm.IsRunning);
    }
}
