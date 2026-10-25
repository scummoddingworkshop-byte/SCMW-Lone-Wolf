using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Testing;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>Adding a section of the mod's own to SCUM's crafting menu.</summary>
public class CraftingMenuTests
{
    private const string Tag = "CraftingCategory.Items.SCMW_Mods";
    private const string MenuData = "SCUM/Content/ConZ_Files/UI/Crafting/CraftingUIData";

    [Fact] // the line has to match the game's own shape exactly
    public void A_tag_line_matches_the_games_own_shape() =>
        Assert.Equal("+GameplayTagList=(Tag=\"CraftingCategory.Items.SCMW_Mods\",DevComment=\"\")",
            CraftingMenuWriter.TagLine(Tag));

    [Fact] // adding keeps every existing line and appends one per new tag
    public void Adding_tags_keeps_the_file_and_appends_one_line_per_tag()
    {
        const string original = "[GameplayTags]\n+GameplayTagList=(Tag=\"A\",DevComment=\"\")\n";

        Assert.True(CraftingMenuWriter.TryAddTags(original,
            new[] { Tag, "CraftingCategory.Items.Other" }, out var updated, out var error), error);

        Assert.StartsWith("[GameplayTags]\n+GameplayTagList=(Tag=\"A\",DevComment=\"\")", updated);
        Assert.Contains(CraftingMenuWriter.TagLine(Tag), updated);
        Assert.Contains(CraftingMenuWriter.TagLine("CraftingCategory.Items.Other"), updated);
    }

    [Fact] // a tag already in the file is left exactly as it is
    public void Adding_a_tag_twice_keeps_one_line()
    {
        Assert.True(CraftingMenuWriter.TryAddTags("[GameplayTags]\n", new[] { Tag }, out var once, out _));

        Assert.True(CraftingMenuWriter.TryAddTags(once, new[] { Tag }, out var twice, out _));

        Assert.Equal(once, twice);
        Assert.Single(twice.Split('\n').Where(l => l.Contains(Tag)));
    }

    [Fact] // a file without the game's tag section is refused, never guessed at
    public void A_file_without_the_games_tag_section_is_refused()
    {
        Assert.False(CraftingMenuWriter.TryAddTags("[Other]\nKey=1\n", new[] { Tag }, out _, out var error));
        Assert.Contains("tag list", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // the same tag in other letter case, or with other words, is already there
    public void A_tag_already_registered_differently_is_left_alone()
    {
        const string original = "[GameplayTags]\n+GameplayTagList=(Tag=\"scmw_mods\",DevComment=\"old\")\n";

        Assert.True(CraftingMenuWriter.TryAddTags(original, new[] { "SCMW_Mods" }, out var updated, out _));

        Assert.Equal(original, updated);
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real tag-list test.")]
    public void The_real_tag_list_keeps_every_line_and_gains_ours()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;

        using var mount = new PakMount();
        Assert.True(mount.TryMount(Path.Combine(gamePath, "SCUM", "Content", "Paks"), key, out var error), error);
        Assert.True(mount.TryReadFile(CraftingMenuWriter.TagsIniPath, out var bytes));

        var original = System.Text.Encoding.UTF8.GetString(bytes!);
        Assert.True(CraftingMenuWriter.TryAddTags(original, new[] { Tag }, out var updated, out var addError),
            addError);

        var after = updated.Split('\n').ToHashSet();
        foreach (var line in original.Split('\n').Where(l => l.Length > 0))
            Assert.Contains(line, after);
        Assert.Contains(CraftingMenuWriter.TagLine(Tag), updated);
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real menu test.")]
    public void A_tag_the_game_already_has_is_refused()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;

        using var mount = new PakMount();
        Assert.True(mount.TryMount(Path.Combine(gamePath, "SCUM", "Content", "Paks"), key, out var error), error);
        Assert.True(mount.TryReadPackage(MenuData, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-menu-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string? uasset = null;
            foreach (var pair in files!)
            {
                var path = Path.Combine(dir, Path.GetFileName(pair.Key));
                File.WriteAllBytes(path, pair.Value);
                if (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) uasset = path;
            }

            var written = new CraftingMenuWriter().AddSections(Assert.IsType<string>(uasset),
                new[] { new NewBuildSection("Melee Weapons", "CraftingCategory.Items.MeleeWeapons") });

            Assert.False(written.Ok);
            Assert.Contains("already", written.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [SkippableFact(EnvVars = "SCMW_SCUM_PATH;SCMW_TEST_AES_KEY",
                   EnvSkipReason = "Set SCMW_SCUM_PATH and SCMW_TEST_AES_KEY to run the real menu test.")]
    public void A_real_menu_data_gets_a_new_section()
    {
        var gamePath = Environment.GetEnvironmentVariable("SCMW_SCUM_PATH")!;
        var key = Environment.GetEnvironmentVariable("SCMW_TEST_AES_KEY")!;

        using var mount = new PakMount();
        Assert.True(mount.TryMount(Path.Combine(gamePath, "SCUM", "Content", "Paks"), key, out var error), error);
        Assert.True(mount.TryReadPackage(MenuData, out var files));

        var dir = Path.Combine(Path.GetTempPath(), "scmw-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string? uasset = null;
            foreach (var pair in files!)
            {
                var path = Path.Combine(dir, Path.GetFileName(pair.Key));
                File.WriteAllBytes(path, pair.Value);
                if (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) uasset = path;
            }

            var written = new CraftingMenuWriter().AddSections(Assert.IsType<string>(uasset),
                new[] { new NewBuildSection("SCMW Mods", Tag) });
            Assert.True(written.Ok, written.Error);

            var check = new UAsset(uasset, EngineVersion.VER_UE4_27);
            var export = check.Exports.OfType<NormalExport>().Single();
            var order = (ArrayPropertyData)export.Data.Single(d => d.Name.ToString() == "ItemCategoryOrder");
            var tags = order.Value.OfType<StructPropertyData>()
                .Select(s => ((NamePropertyData)s.Value.Single()).Value.ToString()).ToList();
            Assert.Contains(Tag, tags);

            var names = (MapPropertyData)export.Data.Single(d => d.Name.ToString() == "CategoryNames");
            TextPropertyData? ours = null;
            foreach (var pair in names.Value)
            {
                var tagName = ((NamePropertyData)((StructPropertyData)pair.Key).Value.Single()).Value.ToString();
                if (tagName == Tag) ours = (TextPropertyData)pair.Value;
            }
            Assert.NotNull(ours);
            Assert.Equal("SCMW Mods", ours!.CultureInvariantString?.ToString());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
