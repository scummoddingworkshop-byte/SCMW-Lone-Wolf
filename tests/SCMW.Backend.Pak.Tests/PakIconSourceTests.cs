// tests/SCMW.Backend.Pak.Tests/PakIconSourceTests.cs
// Focused tests for the real PakIconSource's retryability rule (fix round 1,
// finding 1): a `_ES` sibling that exists but fails to load must stay retryable
// (IconSourceOutcome.NotLoaded), while a genuinely absent sibling is a permanent
// NoIcon. These drive the same decision the extractor relies on, without needing
// a real game install or CUE4Parse objects.
using CUE4Parse.UE4.Assets.Exports;
using SCMW.Backend.Pak;
using Xunit;

public class PakIconSourceTests
{
    private const string Bow = "SCUM/Content/ConZ_Files/Items/Weapons/Ranged_Weapons/Improvised_Bow";

    [Fact]
    public void SiblingExists_is_true_when_the_ES_uasset_is_in_the_mounted_file_list()
    {
        var mount = new FakeMount();
        mount.Paths.Add(Bow + "_ES.uasset");
        var source = new PakIconSource(mount);

        Assert.True(source.SiblingExists(Bow));
        // A caller may also pass the .uasset-suffixed form; both must resolve.
        Assert.True(source.SiblingExists(Bow + ".uasset"));
    }

    [Fact]
    public void SiblingExists_is_false_when_the_ES_uasset_is_absent()
    {
        var mount = new FakeMount();
        mount.Paths.Add(Bow + ".uasset"); // only the item itself, no _ES sibling
        var source = new PakIconSource(mount);

        Assert.False(source.SiblingExists(Bow));
    }

    [Fact]
    public void Sibling_present_but_unloadable_is_retryable_not_permanent_no_icon()
    {
        var outcome = PakIconSource.ResolveSiblingOutcome(
            siblingExists: true, siblingLoaded: false, siblingHasIcon: false, siblingIconPath: null);

        Assert.Equal(IconSourceOutcome.NotLoaded, outcome.Outcome);
    }

    [Fact]
    public void Sibling_absent_is_a_permanent_no_icon()
    {
        var outcome = PakIconSource.ResolveSiblingOutcome(
            siblingExists: false, siblingLoaded: false, siblingHasIcon: false, siblingIconPath: null);

        Assert.Equal(IconSourceOutcome.NoIcon, outcome.Outcome);
    }

    [Fact]
    public void Sibling_loaded_without_an_icon_is_a_permanent_no_icon()
    {
        var outcome = PakIconSource.ResolveSiblingOutcome(
            siblingExists: true, siblingLoaded: true, siblingHasIcon: false, siblingIconPath: null);

        Assert.Equal(IconSourceOutcome.NoIcon, outcome.Outcome);
    }

    [Fact]
    public void Sibling_loaded_with_an_icon_is_resolved()
    {
        var outcome = PakIconSource.ResolveSiblingOutcome(
            siblingExists: true, siblingLoaded: true, siblingHasIcon: true,
            siblingIconPath: "/Game/ConZ_Files/Models/ICO_Bow.ICO_Bow");

        Assert.Equal(IconSourceOutcome.Resolved, outcome.Outcome);
        Assert.Equal("/Game/ConZ_Files/Models/ICO_Bow.ICO_Bow", outcome.TexturePath);
    }

    [Fact]
    public void Resolve_reports_not_loaded_when_the_item_cdo_cannot_be_read()
    {
        var mount = new FakeMount(); // every load returns null
        var source = new PakIconSource(mount);

        var outcome = source.Resolve(Bow);

        Assert.Equal(IconSourceOutcome.NotLoaded, outcome.Outcome);
    }

    [Fact]
    public void A_recipes_product_path_normalises_to_the_base_elements_package_path()
    {
        Assert.Equal(
            "SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well",
            PakIconSource.NormalizeProductPath(
                "/Game/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well.BP_Base_Well_C"));
    }

    [Fact]
    public void A_blank_product_path_normalises_to_null()
        => Assert.Null(PakIconSource.NormalizeProductPath(""));

    private sealed class FakeMount : IPakMount
    {
        public List<string> Paths { get; } = new();

        public bool IsMounted => true;

        public int FileCount => Paths.Count;

        public IEnumerable<string> FilePaths => Paths;

        public bool TryMount(string pakPath, string? aesKey, out string? error)
        {
            error = null;
            return true;
        }

        public bool TryLoadPackageObject(string path, out UObject? obj)
        {
            obj = null; // no CDO loads in these tests
            return false;
        }

        public void Dispose() { }
    }
}
