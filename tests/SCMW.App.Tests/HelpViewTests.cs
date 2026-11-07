using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

        Assert.Contains("What can I make right now in SCMW — Lone Wolf?", topics);
        Assert.Contains("First time here?", topics);
        Assert.Contains("Getting your unlock key", topics);
        Assert.Contains("The whole journey, in four steps", topics);
        Assert.Contains("Make your first change", topics);
        Assert.Contains("Build your mod", topics);
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

    [AvaloniaFact]
    public void Help_describes_the_real_flow_and_promises_nothing_hidden()
    {
        var view = new HelpView();
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();

        // Open every topic, so the check sees all the body text, not just the headers.
        foreach (var expander in view.GetVisualDescendants().OfType<Expander>())
            expander.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var help = string.Join("\n", view.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)));

        // The polish pass removed these buttons; Help must not tell players to press them.
        Assert.DoesNotContain("Install into SCUM", help);
        Assert.DoesNotContain("Update the copy in SCUM", help);
        Assert.DoesNotContain("Remove from SCUM", help);

        // "Make something new" left the Workspace in v2.31.0 — the rail's MAKE rows are the road.
        Assert.DoesNotContain("Make something new", help);
        Assert.Contains("New crafting recipe", help);

        // Loot & spawn is built but not offered, and a recipe's ingredients are shown but locked —
        // so Help must not promise either.
        Assert.DoesNotContain("loot and spawn", help);
        Assert.DoesNotContain("tweak a recipe's ingredients", help);
    }
}
