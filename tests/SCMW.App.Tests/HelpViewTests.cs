using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class HelpViewTests
{
    [AvaloniaFact]
    public void Help_lists_the_main_topics()
    {
        var view = new HelpView();
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();

        var topics = view.GetVisualDescendants().OfType<Expander>()
            .Select(e => e.Header as string).ToList();

        Assert.Contains("First time here?", topics);
        Assert.Contains("Getting your unlock key", topics);
        Assert.Contains("The whole journey, in four steps", topics);
        Assert.Contains("Make your first change", topics);
        Assert.Contains("Build and install your mod", topics);
        Assert.Contains("Turn mods on or off, and play", topics);
        Assert.Contains("Sandbox, servers and mods", topics);
        Assert.Contains("Switching a mod off, or removing it", topics);
        Assert.Contains("Where your work is kept", topics);
        Assert.Contains("Updating", topics);
        Assert.Contains("Uninstalling", topics);
        Assert.Contains("When something looks wrong", topics);
        Assert.Contains("Word list", topics);
    }

    [AvaloniaFact]
    public void Help_topics_start_collapsed()
    {
        var view = new HelpView();
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();

        var expanded = view.GetVisualDescendants().OfType<Expander>().Where(e => e.IsExpanded).ToList();

        Assert.Empty(expanded);   // every topic hides its content until clicked
    }
}
