using SCMW.Backend.Build;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// Writing the words a game text shows. An item's name is a Base text (the words are the source
/// string); its description is often a string-table lookup, which must become plain words instead of
/// pointing at the donor's table entry.
/// </summary>
public class TextValueTests
{
    private readonly FakeNameMap _names = new();

    [Fact] // the game resolves a name through its KEY, so new words need a new key (seen in game 2026-10-16)
    public void A_base_text_gets_the_words_it_shows_and_a_fresh_key()
    {
        var text = new TextPropertyData(_names.Name("Caption"))
        {
            HistoryType = TextHistoryType.Base,
            Value = FString.FromString("CD57C0AF4885DFEBD96E87B331933A6B"),
            CultureInvariantString = FString.FromString("Vegetable Oil"),
        };

        Assert.True(TextValue.TrySet(text, "Olive Oil", out var error), error);

        Assert.Equal(TextHistoryType.Base, text.HistoryType);
        Assert.NotEqual("CD57C0AF4885DFEBD96E87B331933A6B", text.Value?.ToString());
        Assert.Equal("Olive Oil", text.CultureInvariantString.ToString());
    }

    [Fact]
    public void A_string_table_text_becomes_plain_words_with_its_own_key()
    {
        var text = new TextPropertyData(_names.Name("Description"))
        {
            HistoryType = TextHistoryType.StringTableEntry,
            TableId = _names.Name("/Game/ConZ_Files/Items/StringTables/ST_ItemsNoNamespace.ST_ItemsNoNamespace"),
            Value = FString.FromString("8FB4C728443A5BFE669A48A6EF810EB9"),
        };

        Assert.True(TextValue.TrySet(text, "Oil pressed from olives.", out var error), error);

        Assert.Equal(TextHistoryType.Base, text.HistoryType);
        Assert.Null(text.TableId);
        Assert.Equal("Oil pressed from olives.", text.CultureInvariantString.ToString());
        Assert.False(string.IsNullOrEmpty(text.Value?.ToString()));
        Assert.NotEqual("8FB4C728443A5BFE669A48A6EF810EB9", text.Value.ToString());
    }

    [Fact]
    public void A_text_built_from_other_values_is_refused()
    {
        var text = new TextPropertyData(_names.Name("Caption"))
        {
            HistoryType = TextHistoryType.OrderedFormat,
            CultureInvariantString = FString.FromString("Vegetable Oil"),
        };

        Assert.False(TextValue.TrySet(text, "Olive Oil", out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(TextHistoryType.OrderedFormat, text.HistoryType);
        Assert.Equal("Vegetable Oil", text.CultureInvariantString.ToString());
    }

    [Fact]
    public void Empty_words_are_refused()
    {
        var text = new TextPropertyData(_names.Name("Caption")) { HistoryType = TextHistoryType.Base };

        Assert.False(TextValue.TrySet(text, "   ", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
