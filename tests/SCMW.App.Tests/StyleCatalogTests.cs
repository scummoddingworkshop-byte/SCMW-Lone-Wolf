using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class StyleCatalogTests
{
    [AvaloniaFact]
    public void Style_catalog_shows_every_component()
    {
        var view = new StyleCatalogView();

        var texts = view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var label in new[] { "Primary button", "Secondary button", "Card",
                                      "Quick action", "Chip", "Input", "List row" })
        {
            Assert.Contains(label, texts);
        }
    }
}
