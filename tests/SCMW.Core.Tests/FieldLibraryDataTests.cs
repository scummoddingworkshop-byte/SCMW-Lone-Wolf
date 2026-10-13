// tests/SCMW.Core.Tests/FieldLibraryDataTests.cs
// Task 2.2: the shipped library describes the fields that matter, from the research.
using SCMW.Core.Knowledge;
using Xunit;

namespace SCMW.Core.Tests;

public class FieldLibraryDataTests
{
    private static readonly FieldLibrary Library = FieldLibraryLoader.LoadDefault();

    [Fact]
    public void Items_weight_is_described_in_grams_and_writable()
    {
        var def = Library.Lookup("Items", "Weight");

        Assert.NotNull(def);
        Assert.Equal(FieldType.Number, def!.Type);
        Assert.Equal("grams", def.Unit);
        Assert.Equal(Risk.Low, def.Risk);
        Assert.True(def.Writable);
        Assert.False(string.IsNullOrWhiteSpace(def.Label));
        Assert.False(string.IsNullOrWhiteSpace(def.Meaning));
    }

    [Fact]
    public void Items_stack_size_is_described()
    {
        var def = Library.Lookup("Items", "MaxQuantity");

        Assert.NotNull(def);
        Assert.True(def!.Writable);
    }

    [Theory]
    [InlineData("ShelfLife", "in-game hours")]
    [InlineData("_autoDestructTime", "real minutes")]
    public void The_two_item_timers_are_described_with_their_units(string field, string unit)
    {
        var def = Library.Lookup("Items", field);

        Assert.NotNull(def);
        Assert.Equal(unit, def!.Unit);
        Assert.True(def.Writable);
    }

    [Fact]
    public void Base_element_health_and_decay_are_described()
    {
        var health = Library.Lookup("BaseElements", "_healthPoints");
        var decay = Library.Lookup("BaseElements", "_hoursToDecay");

        Assert.NotNull(health);
        Assert.True(health!.Writable);
        Assert.NotNull(decay);
        Assert.Equal("hours", decay!.Unit);
    }

    [Fact]
    public void Recipe_experience_reward_is_described()
    {
        var def = Library.Lookup("Recipes", "ExperienceReward.Basic");

        Assert.NotNull(def);
        Assert.Equal(FieldType.Number, def!.Type);
        Assert.True(def.Writable);
    }

    [Fact]
    public void A_link_field_is_described_as_not_writable_and_risky()
    {
        var product = Library.Lookup("Recipes", "Product");

        Assert.NotNull(product);
        Assert.False(product!.Writable);
        Assert.Equal(Risk.High, product.Risk);
    }

    [Fact]
    public void A_field_named_for_one_kind_is_not_described_for_another()
    {
        Assert.Null(Library.Lookup("Recipes", "Weight"));
    }
}
