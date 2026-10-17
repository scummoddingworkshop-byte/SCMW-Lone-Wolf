using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.Core.Tests;

/// <summary>
/// The picker's tag list: the game's real ingredient tags (<c>CI_*</c> assets) taken straight from
/// the asset index, each with its raw name (the value a recipe stores) and a plain label. The
/// "Tags root" is excluded from the item list, so this is the one place those assets are gathered.
/// </summary>
public class RecipeTagsTests
{
    /// <summary>An indexed asset as the reader emits it: id and technical path without the extension.</summary>
    private static AssetSummary Asset(string packagePath, string category = "Crafting")
    {
        var slash = packagePath.LastIndexOf('/');
        var name = (slash >= 0 ? packagePath[(slash + 1)..] : packagePath).Replace('_', ' ').Trim();
        return new AssetSummary(packagePath, name, category, StatusKind.Unknown, 0,
            packagePath + ".uasset", null);
    }

    [Fact]
    public void No_assets_yields_no_tags()
        => Assert.Empty(RecipeTags.From(Array.Empty<AssetSummary>()));

    [Fact]
    public void A_null_asset_list_yields_no_tags()
        => Assert.Empty(RecipeTags.From(null));

    [Fact]
    public void Only_crafting_tag_assets_become_tags()
    {
        var assets = new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden"),
            Asset("SCUM/Content/ConZ_Files/Items/Weapons/Tomato_ES"),
            Asset("SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well", "Base elements"),
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Metal_Scrap"),
        };

        var tag = Assert.Single(RecipeTags.From(assets));
        Assert.Equal("CI_Metal_Scrap", tag.Name);
    }

    [Fact]
    public void A_tags_name_is_its_raw_name_and_its_label_is_plain_words()
    {
        var tag = Assert.Single(RecipeTags.From(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Metal_Scrap"),
        }));

        Assert.Equal("CI_Metal_Scrap", tag.Name);
        Assert.Equal("Metal Scrap", tag.Label);
    }

    [Fact]
    public void Two_files_with_the_same_tag_name_appear_once()
    {
        var tags = RecipeTags.From(new[]
        {
            Asset("SCUM/Content/A/Ingredients/CI_Plank"),
            Asset("SCUM/Content/B/Ingredients/CI_Plank"),
        });

        Assert.Equal("CI_Plank", Assert.Single(tags).Name);
    }

    [Fact]
    public void Tags_are_sorted_by_their_label()
    {
        var tags = RecipeTags.From(new[]
        {
            Asset("SCUM/Content/Ingredients/CI_Wooden_Stick"),
            Asset("SCUM/Content/Ingredients/CI_Metal_Scrap"),
            Asset("SCUM/Content/Ingredients/CI_Apple"),
        });

        Assert.Equal(new[] { "Apple", "Metal Scrap", "Wooden Stick" },
            tags.Select(t => t.Label).ToArray());
    }

    [Fact]
    public void A_tag_name_gets_a_plain_label()
        => Assert.Equal("Metal Scrap", RecipeTags.Label("CI_Metal_Scrap"));
}
