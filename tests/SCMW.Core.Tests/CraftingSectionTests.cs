using SCMW.Core.Editing;

public class CraftingSectionTests
{
    [Fact] // the owner's words become the game's tag for the Items rail
    public void A_plain_name_becomes_an_items_menu_tag() =>
        Assert.Equal("CraftingCategory.Items.SCMW_Mods", CraftingSectionNaming.TagFor("SCMW Mods"));

    [Fact] // spaces, punctuation and runs become one underscore each
    public void Spaces_and_punctuation_become_single_underscores() =>
        Assert.Equal("CraftingCategory.Items.Olive_Press_2", CraftingSectionNaming.TagFor("  Olive--Press   2 "));

    [Fact] // nothing usable means no tag at all, never a bare prefix
    public void A_name_with_no_letters_or_digits_has_no_tag() =>
        Assert.Null(CraftingSectionNaming.TagFor("---"));

    [Fact] // the words shown on screen are the owner's words, tidied
    public void The_shown_words_are_the_name_tidied() =>
        Assert.Equal("SCMW Mods", CraftingSectionNaming.WordsFor("  SCMW   Mods "));

    [Fact] // a game shelf's tag cannot be reused for a project section
    public void An_existing_game_tag_is_reported_as_taken() =>
        Assert.True(CraftingSectionNaming.IsTaken("craftingcategory.items.toolsandmisc",
            new[] { "CraftingCategory.Items.ToolsAndMisc" }));

    [Fact] // a tag nobody uses is free
    public void An_unknown_tag_is_not_taken() =>
        Assert.False(CraftingSectionNaming.IsTaken("CraftingCategory.Items.SCMW_Mods",
            new[] { "CraftingCategory.Items.ToolsAndMisc" }));
}
