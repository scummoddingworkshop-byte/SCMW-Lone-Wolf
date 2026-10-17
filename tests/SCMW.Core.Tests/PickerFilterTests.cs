using SCMW.Core;

namespace SCMW.Core.Tests;

/// <summary>
/// The one rule behind "type to filter" in every picker: match the plain label, ignoring case and
/// surrounding spaces. Kept pure so the shared control's behaviour is tested without a screen.
/// </summary>
public class PickerFilterTests
{
    private sealed record Item(string Label) : IPickerItem;

    private static IReadOnlyList<Item> Items() => new[]
    {
        new Item("Apple"),
        new Item("Banana"),
        new Item("Green Apple"),
        new Item("Metal Scrap"),
    };

    [Fact]
    public void A_blank_search_shows_everything()
        => Assert.Equal(4, PickerFilter.Apply(Items(), "").Count);

    [Fact]
    public void A_null_search_shows_everything()
        => Assert.Equal(4, PickerFilter.Apply(Items(), null).Count);

    [Fact]
    public void A_null_list_yields_nothing()
        => Assert.Empty(PickerFilter.Apply<Item>(null, "a"));

    [Fact]
    public void Only_matching_labels_are_kept()
    {
        var matches = PickerFilter.Apply(Items(), "app");

        Assert.Equal(new[] { "Apple", "Green Apple" }, matches.Select(m => m.Label).ToArray());
    }

    [Fact]
    public void Matching_ignores_case()
        => Assert.Equal(2, PickerFilter.Apply(Items(), "APPLE").Count);

    [Fact]
    public void Matching_ignores_surrounding_spaces()
        => Assert.Equal(2, PickerFilter.Apply(Items(), "  apple  ").Count);

    [Fact]
    public void A_match_in_the_middle_counts()
        => Assert.Single(PickerFilter.Apply(Items(), "scrap"));

    [Fact]
    public void A_search_with_no_match_yields_nothing()
        => Assert.Empty(PickerFilter.Apply(Items(), "zzz"));
}
