using SCMW.Backend.Pak;
using Xunit;

public class CategoryTests
{
    [Theory]
    [InlineData("ItemSortGroup.Food.Solid", "Food")]
    [InlineData("ItemSortGroup.Food.Liquid", "Food")]
    [InlineData("ItemSortGroup.Weapon.Attachment.Magazine", "Weapon")]
    [InlineData("ItemSortGroup.CraftingMaterials", "Crafting Materials")]
    [InlineData("ItemSortGroup.AnimalParts", "Animal Parts")]
    [InlineData("ItemSortGroup.AmmoBox", "Ammo Box")]
    [InlineData("ItemSortGroup.WeaponParts", "Weapon Parts")]
    [InlineData("ItemSortGroup.Other", "Other")]
    [InlineData("Whatever", "Whatever")]
    public void Sort_group_becomes_a_friendly_top_level_category(string value, string expected)
        => Assert.Equal(expected, PakContentSource.FriendlySortGroup(value));

    [Theory]
    [InlineData("Weapons", "Weapon")]
    [InlineData("Ammunition", "Ammo")]
    [InlineData("Medications", "Medical")]
    [InlineData("Grenades", "Explosives")]
    [InlineData("Equipment", "Other")]         // tiny folder-only categories fold into the catch-all
    [InlineData("Economy", "Other")]
    [InlineData("TwitchDrops", "Other")]
    [InlineData("Food", "Food")]
    public void Folder_categories_are_aliased_onto_the_sort_group_words(string folder, string expected)
        => Assert.Equal(expected, PakContentSource.Canonical(folder));
}
