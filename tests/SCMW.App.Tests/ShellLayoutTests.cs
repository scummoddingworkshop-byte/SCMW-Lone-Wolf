using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace SCMW.App.Tests;

public class ShellLayoutTests
{
    [AvaloniaFact]
    public void Window_shows_the_areas_and_the_launch_buttons()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        foreach (var area in new[] { "Home", "Projects", "Items", "Build", "Mods", "Backups", "Settings", "Help" })
        {
            Assert.Contains(area, labels);
        }

        Assert.Contains("LAUNCH MODDED", labels);
        Assert.Contains("LAUNCH UNMODDED", labels);
    }
}
