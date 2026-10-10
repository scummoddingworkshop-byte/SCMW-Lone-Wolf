using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class IconButtonTests
{
    [AvaloniaFact]
    public void Icon_button_keeps_its_icon_footprint_without_an_icon()
    {
        var button = new IconButton { Text = "Launch modded" };
        button.Measure(new Size(240, 40));

        var labels = button.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.Contains("Launch modded", labels);

        var image = button.GetLogicalDescendants().OfType<Image>().Single();
        Assert.False(image.IsVisible);   // placeholder only; no real icon yet
        Assert.Equal(16, image.Width);   // the reserved footprint
    }
}
