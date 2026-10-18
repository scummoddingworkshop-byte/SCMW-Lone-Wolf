using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// A cloned asset must not keep pointing at the donor. An item's "_ES" companion names its item in a
/// soft reference (its "Actor" property); left alone, the copy still claims the donor item, so the
/// game binds the copy's setup to the donor and the new item gets none (empty Examine, missing
/// components). <see cref="AssetReferenceRemap.RemapPath"/> is the pure rule that re-points such a path.
/// </summary>
public class AssetReferenceRemapTests
{
    private static readonly AssetReferenceRemap Item =
        new("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil",
            "/Game/ConZ_Files/Items/Food/MiscFood/SCMW_MyStoneAxeTEST/OliveOilTEST",
            "VegetableOil", "OliveOilTEST");

    [Theory]
    // the class reference the "_ES" really carries (…<package>.<name>_C)
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil_C",
                "/Game/ConZ_Files/Items/Food/MiscFood/SCMW_MyStoneAxeTEST/OliveOilTEST.OliveOilTEST_C")]
    // the plain object form
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil",
                "/Game/ConZ_Files/Items/Food/MiscFood/SCMW_MyStoneAxeTEST/OliveOilTEST.OliveOilTEST")]
    // that object's class-default twin
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.Default__VegetableOil_C",
                "/Game/ConZ_Files/Items/Food/MiscFood/SCMW_MyStoneAxeTEST/OliveOilTEST.Default__OliveOilTEST_C")]
    // a package reference with no object half
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil",
                "/Game/ConZ_Files/Items/Food/MiscFood/SCMW_MyStoneAxeTEST/OliveOilTEST")]
    public void A_reference_to_the_donor_is_re_pointed_at_the_copy(string input, string expected)
        => Assert.Equal(expected, Item.RemapPath(input));

    [Theory]
    // the donor's own meshes/icons live in a *different* package — never touched
    [InlineData("/Game/ConZ_Files/Models/Objects/Items/Crafting_Items/VegetableOil/ICO_Vegetable_Oil.ICO_Vegetable_Oil")]
    // the donor's "_ES" twin is a different package (its own remap handles it)
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil_ES.VegetableOil_ES_C")]
    // a sibling whose name merely starts the same
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil2.VegetableOil2_C")]
    // nothing to do
    [InlineData("/Game/ConZ_Files/Items/Food/MiscFood/Coffee.Coffee_C")]
    public void A_reference_to_something_else_is_left_alone(string input)
        => Assert.Null(Item.RemapPath(input));
}
