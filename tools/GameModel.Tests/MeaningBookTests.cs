namespace SCMW.GameModel;

/// <summary>Reads the human-owned meanings.md into a field → meaning lookup.</summary>
public class MeaningBookTests
{
    private const string Sample = """
        # Meanings

        Some prose that is not a table.

        ## items

        | field | meaning | confidence |
        |---|---|---|
        | `Weight` | How heavy it is. | Assumed |
        | `MaxQuantity` | Biggest stack. | Tested |

        ## recipes

        | field | meaning | confidence |
        |---|---|---|
        | `Product` | What it makes. | Assumed |
        """;

    [Fact]
    public void Reads_a_meaning_for_its_family_and_field()
    {
        var book = MeaningBook.Parse(Sample);

        Assert.Equal("How heavy it is.", book[("items", "Weight")].Text);
        Assert.Equal("Tested", book[("items", "MaxQuantity")].Confidence);
        Assert.Equal("What it makes.", book[("recipes", "Product")].Text);
    }

    [Fact]
    public void Keeps_the_same_field_name_apart_in_different_families()
    {
        var book = MeaningBook.Parse(Sample);

        Assert.True(book.ContainsKey(("recipes", "Product")));
        Assert.False(book.ContainsKey(("items", "Product")));
    }

    [Fact]
    public void Ignores_the_header_and_separator_rows()
        => Assert.DoesNotContain(("items", "field"), MeaningBook.Parse(Sample).Keys);

    [Fact]
    public void Unescapes_a_pipe_written_inside_a_meaning()
    {
        var book = MeaningBook.Parse("""
            ## items

            | field | meaning | confidence |
            |---|---|---|
            | `Weight` | mass \| in grams | Assumed |
            """);

        Assert.Equal("mass | in grams", book[("items", "Weight")].Text);
        Assert.Equal("Assumed", book[("items", "Weight")].Confidence);
    }
}
