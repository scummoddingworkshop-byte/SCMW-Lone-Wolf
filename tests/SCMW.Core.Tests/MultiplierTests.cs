using SCMW.Core.Editing;

namespace SCMW.Core.Tests;

/// <summary>
/// The plain-language factor a player types for a multiply — "×2", "half", "+50%" — turned into the
/// number the change is recorded with. Words, a multiplier, or a percentage all mean the same thing.
/// </summary>
public class MultiplierTests
{
    [Theory]
    [InlineData("2", 2)]
    [InlineData("0.5", 0.5)]
    [InlineData("×2", 2)]
    [InlineData("x2", 2)]
    [InlineData("half", 0.5)]
    [InlineData("double", 2)]
    [InlineData("triple", 3)]
    [InlineData("+50%", 1.5)]
    [InlineData("50%", 1.5)]
    [InlineData("-25%", 0.75)]
    public void A_factor_is_read_from_the_words_a_player_types(string text, double expected)
    {
        Assert.True(Multiplier.TryParse(text, out var factor));
        Assert.Equal(expected, factor, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("%")]
    [InlineData("half a")]
    public void A_factor_that_makes_no_sense_is_refused(string text)
        => Assert.False(Multiplier.TryParse(text, out _));

    [Fact]
    public void A_missing_factor_is_refused()
        => Assert.False(Multiplier.TryParse(null, out _));

    [Fact] // a number too large to represent is not a usable factor
    public void A_factor_that_is_not_a_finite_number_is_refused()
        => Assert.False(Multiplier.TryParse("1e400", out _));
}
