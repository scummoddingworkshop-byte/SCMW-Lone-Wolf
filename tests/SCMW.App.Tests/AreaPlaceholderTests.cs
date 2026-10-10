using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

namespace SCMW.App.Tests;

public class AreaPlaceholderTests
{
    [AvaloniaFact]
    public void Placeholder_shows_its_title()
    {
        var view = new AreaPlaceholderView { Title = "Mods" };
        view.Measure(new Size(400, 200));

        var labels = view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text);

        Assert.Contains("Mods", labels);
    }
}
