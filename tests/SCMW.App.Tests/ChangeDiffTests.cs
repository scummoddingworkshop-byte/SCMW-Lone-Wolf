using SCMW.Core;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

/// <summary>
/// The one list Build and Projects both read must keep a fixed order, so two items can be compared
/// line for line. These tests pin that order.
/// </summary>
public class ChangeDiffTests
{
    private static ChangeRecord Rec(string path, string field, string oldValue, string newValue,
                                    DateTime? at = null) =>
        new(Guid.NewGuid(), "p", path, field, oldValue, newValue,
            at ?? DateTime.UtcNow, StatusKind.Editable, AssetPath: path);

    [Fact] // values sit alphabetically on every card, not in the order they were changed
    public void Value_lines_are_alphabetical()
    {
        var items = ChangeDiff.Build(new[]
        {
            Rec("/g/Zeta_ES", "Max stack", "10", "25"),
            Rec("/g/Zeta_ES", "GridInventoryRowSpan", "2", "3"),
        });

        var values = Assert.Single(items).Values;
        Assert.Equal(new[] { "Height in grid", "Max Stack" },
                     values.Select(v => v.ValueName));
    }

    [Fact] // item cards sit alphabetically too, so an item is always in the same place
    public void Item_cards_are_alphabetical()
    {
        var items = ChangeDiff.Build(new[]
        {
            Rec("/g/Zeta_ES", "Weight", "5", "3"),
            Rec("/g/Alpha_ES", "Weight", "4", "2"),
        });

        Assert.Equal(new[] { "/g/Alpha_ES", "/g/Zeta_ES" }, items.Select(i => i.ItemName));
    }
}
