using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class ProjectTileTests
{
    [AvaloniaFact]
    public void Tile_shows_details_and_a_current_chip_only_when_current()
    {
        var project = new ProjectSummary("Duct Tape Expanded", "Bigger stacks", "2 hours ago");
        var tile = new ProjectTile { Project = project };
        tile.Measure(new Size(300, 320));

        var texts = tile.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Duct Tape Expanded", texts);
        Assert.Contains("Bigger stacks", texts);
        Assert.Contains("2 hours ago", texts);

        var chip = tile.FindControl<Border>("CurrentChip")!;
        Assert.False(chip.IsVisible);

        project.IsCurrent = true;
        Assert.True(chip.IsVisible);
    }

    [AvaloniaFact]
    public void Tile_shows_the_default_mark_when_there_is_no_picture()
    {
        var tile = new ProjectTile { Project = new ProjectSummary("n", "d", "w") };
        tile.Measure(new Size(300, 320));

        Assert.False(tile.GetLogicalDescendants().OfType<Image>().Single().IsVisible);
        Assert.True(tile.FindControl<TextBlock>("DefaultMark")!.IsVisible);
    }

    [AvaloniaFact]
    public void The_picture_slot_is_square_so_pictures_are_never_cropped()
    {
        var tile = new ProjectTile { Project = new ProjectSummary("n", "d", "w") };
        tile.Measure(new Size(300, 320));

        var slot = tile.FindControl<Border>("PictureSlot")!;
        Assert.Equal(slot.Width, slot.Height);
    }

    [AvaloniaFact]
    public void The_menu_says_Edit_not_Rename()
    {
        var tile = new ProjectTile { Project = new ProjectSummary("n", "d", "w") };
        tile.Measure(new Size(300, 320));

        var menu = (MenuFlyout)tile.FindControl<Button>("MenuButton")!.Flyout!;
        var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header).ToList();
        Assert.Contains("Edit", headers);
        Assert.DoesNotContain("Rename", headers);
    }

    [AvaloniaFact]
    public void Tile_shows_the_delete_confirmation_only_when_asked()
    {
        var project = new ProjectSummary("n", "d", "w");
        var tile = new ProjectTile { Project = project };
        tile.Measure(new Size(300, 320));

        var bar = tile.FindControl<StackPanel>("ConfirmBar")!;
        Assert.False(bar.IsVisible);

        project.IsConfirmingDelete = true;
        Assert.True(bar.IsVisible);
    }

    [AvaloniaFact]
    public void Tile_contains_no_button_inside_a_button()
    {
        var tile = new ProjectTile { Project = new ProjectSummary("n", "d", "w") };
        tile.Measure(new Size(300, 320));

        foreach (var button in tile.GetLogicalDescendants().OfType<Button>())
        {
            Assert.Empty(button.GetLogicalDescendants().OfType<Button>());
        }
    }
}
