using SCMW.Core;
using Xunit;

namespace SCMW.Core.Tests;

/// <summary>
/// The plain number the app shows for a value: a whole number stays as it is, and a number with a
/// fraction is shown with three places after the dot, so float noise (0.15000000000000002) never
/// reaches the owner's eyes. Display only — what gets written is the value the file holds.
/// </summary>
public class PlainNumberTests
{
    [Theory]
    [InlineData("150", "150")]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("-3", "-3")]
    public void A_whole_number_is_left_alone(string raw, string shown) =>
        Assert.Equal(shown, PlainNumber.ForDisplay(raw));

    [Theory]
    [InlineData("0.15000000000000002", "0.150")]
    [InlineData("0.15", "0.150")]
    [InlineData("0.1", "0.100")]
    [InlineData("1.5", "1.500")]
    [InlineData("12.5", "12.500")]
    [InlineData("0.3333333333", "0.333")]
    [InlineData("2.0005", "2.001")]
    [InlineData("-0.15", "-0.150")]
    public void A_fraction_is_shown_with_three_places(string raw, string shown) =>
        Assert.Equal(shown, PlainNumber.ForDisplay(raw));

    [Theory]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/BlackPepper.BlackPepper_C")]
    [InlineData("Ingredient_Oil")]
    [InlineData("True")]
    public void Anything_that_is_not_a_plain_number_is_shown_as_it_is(string raw) =>
        Assert.Equal(raw, PlainNumber.ForDisplay(raw));

    [Fact]
    public void A_trailing_zero_fraction_shows_as_a_whole_number() =>
        Assert.Equal("2", PlainNumber.ForDisplay("2.0"));
}
