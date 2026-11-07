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
    public void The_picture_slot_is_a_wide_banner_not_square()
    {
        var tile = new ProjectTile { Project = new ProjectSummary("n", "d", "w") };
        var window = new Window { Content = tile, Width = 300, Height = 320 };
        window.Show();

        var slot = tile.FindControl<Border>("PictureSlot")!;
        Assert.True(slot.Bounds.Height > 0);
        Assert.True(slot.Bounds.Width > slot.Bounds.Height);   // a wide banner, not a square
    }

    [AvaloniaFact]
    public void The_card_offers_its_actions_as_plain_buttons()
    {
        var tile = new ProjectTile { Project = new ProjectSummary("n", "d", "w") };
        tile.Measure(new Size(300, 320));

        // No menu to hunt for: the three actions are buttons on the card itself.
        Assert.Null(tile.FindControl<Button>("MenuButton"));
        Assert.Null(tile.FindControl<Button>("OpenButton"));

        var labels = new[] { "EditButton", "DuplicateButton", "DeleteButton" }
            .Select(name => tile.FindControl<Button>(name)?.Content as string)
            .ToList();

        Assert.Equal(new[] { "Edit", "Duplicate", "Delete" }, labels);
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

    [AvaloniaFact] // the owner's toggle: switch a project's mod on or off from its own card
    public void The_card_has_a_switch_for_its_mod_once_it_has_been_built()
    {
        var project = new ProjectSummary("n", "d", "w") { HasMod = true, ModOn = true };
        var tile = new ProjectTile { Project = project };
        var window = new Window { Content = tile, Width = 300, Height = 320 };
        window.Show();

        var toggle = tile.FindControl<ToggleSwitch>("ModSwitch");
        Assert.NotNull(toggle);
        Assert.True(toggle!.IsVisible);
        Assert.True(toggle.IsEnabled);
        Assert.True(toggle.IsChecked);
        Assert.Equal("On — SCUM loads this mod", ToolTip.GetTip(toggle));
    }

    [AvaloniaFact] // a card with no mod yet keeps the switch in sight, greyed, and says why
    public void An_unbuilt_project_shows_a_greyed_switch()
    {
        var project = new ProjectSummary("n", "d", "w") { HasMod = false };
        var tile = new ProjectTile { Project = project };
        var window = new Window { Content = tile, Width = 300, Height = 320 };
        window.Show();

        var toggle = tile.FindControl<ToggleSwitch>("ModSwitch");
        Assert.NotNull(toggle);
        Assert.True(toggle!.IsVisible);
        Assert.False(toggle.IsEnabled);
        Assert.False(toggle.IsChecked);
        Assert.Equal("Build this mod first", ToolTip.GetTip(toggle));
    }

    [AvaloniaFact] // the switch is the card's own control: flipping it must not also pick the project
    public void A_click_on_the_switch_is_not_a_click_on_the_card()
    {
        var project = new ProjectSummary("n", "d", "w") { HasMod = true };
        var tile = new ProjectTile { Project = project };
        var window = new Window { Content = tile, Width = 300, Height = 320 };
        window.Show();

        var toggle = tile.FindControl<ToggleSwitch>("ModSwitch")!;
        var plain = tile.FindControl<TextBlock>("ChangesText")!;

        Assert.False(ProjectTile.OpensTheCard(toggle));
        Assert.True(ProjectTile.OpensTheCard(plain));
    }
}
