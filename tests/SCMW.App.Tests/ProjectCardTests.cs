using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class ProjectCardTests
{
    [AvaloniaFact]
    public void Project_card_shows_its_details()
    {
        var card = new ProjectCard
        {
            Project = new ProjectSummary("Duct Tape Expanded", "Bigger stacks", "2 hours ago"),
        };
        card.Measure(new Size(240, 200));

        var texts = card.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Duct Tape Expanded", texts);
        Assert.Contains("Bigger stacks", texts);
        Assert.Contains("2 hours ago", texts);
    }

    [AvaloniaFact]
    public void Project_card_shows_the_default_mark_when_there_is_no_picture()
    {
        var card = new ProjectCard
        {
            Project = new ProjectSummary("No picture", "desc", "now"),
        };
        card.Measure(new Size(240, 200));

        var image = card.GetLogicalDescendants().OfType<Image>().Single();
        Assert.False(image.IsVisible);
        Assert.True(card.FindControl<TextBlock>("DefaultMark")!.IsVisible);
    }

    [AvaloniaFact]
    public void Project_card_shows_a_picture_when_one_is_set()
    {
        var picture = new WriteableBitmap(
            new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var card = new ProjectCard
        {
            Project = new ProjectSummary("With picture", "desc", "now", picture),
        };
        card.Measure(new Size(240, 200));

        var image = card.GetLogicalDescendants().OfType<Image>().Single();
        Assert.True(image.IsVisible);
        Assert.Same(picture, image.Source);
    }

    [AvaloniaFact]
    public void Project_card_wraps_long_text()
    {
        var card = new ProjectCard
        {
            Project = new ProjectSummary(
                "A very long project name that should not clip",
                "A description long enough to need more than one line on a narrow card.",
                "5 days ago"),
        };
        card.Measure(new Size(240, 200));

        Assert.Equal(TextWrapping.Wrap, card.FindControl<TextBlock>("DescriptionText")!.TextWrapping);
    }

    [AvaloniaFact]
    public void Project_card_forwards_its_command_and_parameter()
    {
        var nav = new NavigationHost();
        var vm = new HomeViewModel(nav);
        var summary = vm.RecentProjects[0];

        var card = new ProjectCard
        {
            Project = summary,
            Command = vm.OpenRecentProjectCommand,
            CommandParameter = summary,
        };
        card.Measure(new Size(240, 200));

        var inner = card.FindControl<Button>("Inner")!;
        Assert.Same(vm.OpenRecentProjectCommand, inner.Command);
        Assert.Same(summary, inner.CommandParameter);
    }
}
