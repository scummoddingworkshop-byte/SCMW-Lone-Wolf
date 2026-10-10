using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class ItemRowTests
{
    [AvaloniaFact]
    public void Row_shows_the_item_name_and_category()
    {
        var row = new ItemRow
        {
            Item = new ItemSummary("Duct Tape", "Tools", "Sticks things together", editable: true),
        };
        row.Measure(new Size(300, 60));

        var texts = row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Duct Tape", texts);
        Assert.Contains("Tools", texts);
    }

    [AvaloniaFact]
    public void Row_shows_the_default_mark_when_there_is_no_picture()
    {
        var row = new ItemRow
        {
            Item = new ItemSummary("Duct Tape", "Tools", "Sticks", editable: true),
        };
        row.Measure(new Size(300, 60));

        Assert.False(row.GetLogicalDescendants().OfType<Image>().Single().IsVisible);
        Assert.True(row.FindControl<TextBlock>("DefaultMark")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_contains_no_button()
    {
        var row = new ItemRow
        {
            Item = new ItemSummary("Duct Tape", "Tools", "Sticks", editable: true),
        };
        row.Measure(new Size(300, 60));

        Assert.Empty(row.GetLogicalDescendants().OfType<Button>());
    }
}
