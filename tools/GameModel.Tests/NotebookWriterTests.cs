namespace SCMW.GameModel;

/// <summary>Renders the tool-written Markdown notebook pages.</summary>
public class NotebookWriterTests
{
    private static FieldRow Row(string name, string type = "FloatProperty", int assets = 1, bool nested = false, string example = "")
        => new(name, type, assets, nested, example);

    [Fact]
    public void Family_page_states_the_family_the_build_and_the_counts()
    {
        var page = NotebookWriter.FamilyPage(
            "items", new[] { Row("Weight", example: "20000") },
            assetCount: 3202, buildId: "25804202", readDate: "2026-10-15");

        Assert.Contains("# Fields — family `items`", page);
        Assert.Contains("3202", page);
        Assert.Contains("25804202", page);
        Assert.Contains("2026-10-15", page);
        Assert.Contains("Distinct fields: **1**", page);
    }

    [Fact]
    public void Family_page_renders_one_table_row_per_field()
    {
        var page = NotebookWriter.FamilyPage(
            "items",
            new[]
            {
                Row("Weight", "FloatProperty", 3290, example: "20000"),
                Row("GridInventorySortGroup.TagName", "NameProperty", 318, nested: true, example: "ItemSortGroup.Other"),
            },
            assetCount: 3202, buildId: "25804202", readDate: "2026-10-15");

        Assert.Contains("| `Weight` | FloatProperty | 3290 |  | 20000 |", page);
        Assert.Contains("| `GridInventorySortGroup.TagName` | NameProperty | 318 | yes | ItemSortGroup.Other |", page);
    }

    [Fact]
    public void Family_page_escapes_a_pipe_in_an_example_so_the_table_survives()
    {
        var page = NotebookWriter.FamilyPage(
            "items", new[] { Row("Caption", "TextProperty", 1, example: "a|b") },
            assetCount: 1, buildId: "1", readDate: "2026-10-15");

        Assert.Contains("a\\|b", page);
    }

    [Fact]
    public void Family_page_says_zero_when_a_family_has_no_fields()
    {
        var page = NotebookWriter.FamilyPage(
            "chopping", Array.Empty<FieldRow>(),
            assetCount: 0, buildId: "1", readDate: "2026-10-15");

        Assert.Contains("Distinct fields: **0**", page);
    }

    [Fact]
    public void Index_lists_each_family_with_its_counts_and_a_link()
    {
        var page = NotebookWriter.IndexPage(
            new[]
            {
                new FamilySummary("items", Assets: 3202, Fields: 155),
                new FamilySummary("recipes", Assets: 865, Fields: 60),
            },
            buildId: "25804202", readDate: "2026-10-15");

        Assert.Contains("25804202", page);
        Assert.Contains("[`items`](fields/items.md)", page);
        Assert.Contains("[`recipes`](fields/recipes.md)", page);
        Assert.Contains("| 3202 | 155 |", page);
        Assert.Contains("[`links.md`](links.md)", page);
    }

    [Fact]
    public void Family_page_shows_a_meaning_for_each_field()
    {
        var meanings = new Dictionary<string, Meaning> { ["Weight"] = new("How heavy it is.", "Assumed") };

        var authored = NotebookWriter.FamilyPage(
            "items", new[] { Row("Weight") }, 1, "25804202", "2026-10-15", meanings);
        Assert.Contains("How heavy it is. (Assumed)", authored);

        var unauthored = NotebookWriter.FamilyPage(
            "items", new[] { Row("Zeta") }, 1, "25804202", "2026-10-15", meanings);
        Assert.Contains("Assumed (not yet reviewed)", unauthored);
    }

    [Fact]
    public void Links_page_states_the_method_and_renders_a_row_per_edge()
    {
        var page = NotebookWriter.LinksPage(
            new[] { new FamilyLink("recipes", "crafting_tags", 136, 40, "Ingredients[0].AllowedTypes[0]", "CI_Metal_Scrap") },
            buildId: "25804202", readDate: "2026-10-15");

        Assert.Contains("# Links between families", page);
        Assert.Contains("25804202", page);
        Assert.Contains("| `recipes` | `crafting_tags` | 136 | 40 | `Ingredients[0].AllowedTypes[0]` | `CI_Metal_Scrap` |", page);
    }
}
