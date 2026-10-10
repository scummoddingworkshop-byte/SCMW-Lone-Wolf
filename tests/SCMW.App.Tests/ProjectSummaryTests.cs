using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ProjectSummaryTests
{
    [AvaloniaFact]
    public void Project_summary_keeps_its_details_and_notifies_on_state_change()
    {
        var project = new ProjectSummary("Duct Tape", "Bigger stacks", "2 hours ago");

        var changed = new List<string>();
        project.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        project.IsCurrent = true;
        project.IsConfirmingDelete = true;

        Assert.Equal("Duct Tape", project.Name);
        Assert.Equal("Bigger stacks", project.Description);
        Assert.Equal("2 hours ago", project.When);
        Assert.True(project.IsCurrent);
        Assert.True(project.IsConfirmingDelete);
        Assert.Contains(nameof(ProjectSummary.IsCurrent), changed);
        Assert.Contains(nameof(ProjectSummary.IsConfirmingDelete), changed);
    }
}
