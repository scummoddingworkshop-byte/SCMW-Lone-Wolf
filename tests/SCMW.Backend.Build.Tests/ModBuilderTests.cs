using SCMW.Backend.Build;
using SCMW.Core.Editing;

namespace SCMW.Backend.Build.Tests;

public class ModBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-bld-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class FakeReader : IPackageReader
    {
        public readonly Dictionary<string, Dictionary<string, byte[]>> Packages = new();
        public byte[]? Index;
        public bool TryReadPackage(string path, out IReadOnlyDictionary<string, byte[]>? files)
        {
            if (Packages.TryGetValue(path, out var f)) { files = f; return true; }
            files = null; return false;
        }
        public bool TryReadAssetIndex(out byte[]? bytes)
        {
            bytes = Index;
            return Index is not null;
        }
    }

    private sealed class FakeCloner : IAssetCloner
    {
        public bool Ok = true;
        public readonly List<(string Uasset, string OldName, string NewName)> Calls = new();
        public bool TryRenameObjects(string uassetFilePath, string oldName, string newName, out string? error)
        {
            Calls.Add((uassetFilePath, oldName, newName));
            error = Ok ? null : "The copy could not be renamed.";
            return Ok;
        }
    }

    private sealed class FakeWriter : IPackageWriter
    {
        public WriteResult Next = new(true, null);
        public readonly List<FieldChange> Applied = new();
        public IReadOnlyDictionary<string, string>? Donors;
        public WriteResult Apply(string uasset, IReadOnlyList<FieldChange> changes,
                                 IReadOnlyDictionary<string, string>? donors = null)
        {
            if (!Next.Ok) return Next;
            Applied.AddRange(changes);
            Donors = donors;
            File.WriteAllText(uasset, "edited");            // stand in for the real rewrite
            File.WriteAllText(Path.ChangeExtension(uasset, ".uexp"), "edited");
            return Next;
        }
    }

    private sealed class FakePacker : IPacker
    {
        public PackResult Next = new(true, null); public string? Staging; public string? Pak;
        public Task<PackResult> PackAsync(string stagingFolder, string pakPath, CancellationToken ct)
        { Staging = stagingFolder; Pak = pakPath; if (Next.Ok) File.WriteAllText(pakPath, "pak"); return Task.FromResult(Next); }
    }

    private sealed class FakeResolver : IAssetResolver
    {
        public readonly Dictionary<string, AssetResolution> Map = new();
        public Task<AssetResolution> ResolveAsync(string name, CancellationToken ct)
            => Task.FromResult(Map.TryGetValue(name, out var r) ? r : new AssetResolution(null, $"\"{name}\" was not found."));
    }

    private sealed class ListProgress : IProgress<int>
    {
        public readonly List<int> Seen = new();
        public void Report(int value) => Seen.Add(value);
    }

    private (ModBuilder builder, FakeReader reader, FakeWriter writer, FakePacker packer,
             FakeResolver resolver, FakeCloner cloner) New()
    {
        var reader = new FakeReader(); var writer = new FakeWriter();
        var packer = new FakePacker(); var resolver = new FakeResolver();
        var cloner = new FakeCloner();
        return (new ModBuilder(reader, writer, packer, resolver, cloner, Path.Combine(_root, "Builds")),
                reader, writer, packer, resolver, cloner);
    }

    private static void AddPackage(FakeReader reader, string path)
        => reader.Packages[path] = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFileNameWithoutExtension(path) + ".uasset"] = new byte[] { 1 },
            [Path.GetFileNameWithoutExtension(path) + ".uexp"] = new byte[] { 2 },
        };

    [Fact]
    public async Task Build_packs_the_changed_item_on_the_game_tree_and_returns_the_pak()
    {
        var (builder, reader, writer, packer, resolver, _) = New();
        const string pkg = "SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES";
        AddPackage(reader, pkg);
        resolver.Map["Duct Tape ES"] = new AssetResolution(pkg, null);

        var request = new BuildRequest("My first mod", "1.0.0",
            new[] { new BuildItem("Duct Tape ES", new[] { new FieldChange("MaxStackSize", "25") }) });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.EndsWith("SCMW_My-first-mod_v1.0.0_P.pak", outcome.PakPath);
        Assert.Contains(writer.Applied, c => c is { Field: "MaxStackSize", NewValue: "25" });
        Assert.True(File.Exists(Path.Combine(packer.Staging!, "SCUM", "Content", "ConZ_Files",
            "Items", "Equipment", "Duct_Tape_ES.uasset")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(outcome.PakPath)!, "build.json")));
    }

    [Fact]
    public async Task A_writer_that_cannot_set_a_value_fails_the_whole_build_with_no_pak()
    {
        var (builder, reader, writer, packer, resolver, _) = New();
        const string pkg = "SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES";
        AddPackage(reader, pkg);
        resolver.Map["Duct Tape ES"] = new AssetResolution(pkg, null);
        writer.Next = new WriteResult(false, "That kind of value cannot be changed by the mod maker yet.");
        var request = new BuildRequest("M", "1.0.0",
            new[] { new BuildItem("Duct Tape ES", new[] { new FieldChange("Grid", "x") }) });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(outcome.PakPath);
        Assert.Null(packer.Pak);                        // nothing was packed
        Assert.Contains("cannot be changed", outcome.Error);
    }

    [Fact]
    public async Task A_packer_failure_is_reported_and_no_path_is_returned()
    {
        var (builder, reader, _, packer, resolver, _) = New();
        const string pkg = "SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES";
        AddPackage(reader, pkg);
        resolver.Map["Duct Tape ES"] = new AssetResolution(pkg, null);
        packer.Next = new PackResult(false, "The packing tool is missing from the app.");
        var request = new BuildRequest("M", "1.0.0",
            new[] { new BuildItem("Duct Tape ES", new[] { new FieldChange("MaxStackSize", "25") }) });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("packing tool", outcome.Error);
    }

    [Fact]
    public async Task An_empty_project_refuses_to_build()
    {
        var (builder, _, _, _, _, _) = New();
        var outcome = await builder.BuildAsync(
            new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>()), null, CancellationToken.None);
        Assert.False(outcome.Ok);
        Assert.Contains("nothing to build", outcome.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Progress_reports_the_phases_in_order()
    {
        var (builder, reader, _, _, resolver, _) = New();
        AddPackage(reader, "SCUM/Content/ConZ_Files/Items/X_ES");
        resolver.Map["X"] = new AssetResolution("SCUM/Content/ConZ_Files/Items/X_ES", null);
        var progress = new ListProgress();
        var request = new BuildRequest("M", "1.0.0",
            new[] { new BuildItem("X", new[] { new FieldChange("F", "1") }) });

        await builder.BuildAsync(request, progress, CancellationToken.None);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, progress.Seen);
    }

    [Fact]
    public async Task Two_edits_to_the_same_package_are_both_applied()
    {
        var (builder, reader, writer, packer, resolver, _) = New();
        const string pkg = "SCUM/Content/ConZ_Files/Items/Equipment/Duct_Tape_ES";
        AddPackage(reader, pkg);
        resolver.Map["Duct Tape ES"] = new AssetResolution(pkg, null);
        resolver.Map[pkg] = new AssetResolution(pkg, null);   // the same file, recorded by path

        var request = new BuildRequest("M", "1.0.0", new[]
        {
            new BuildItem("Duct Tape ES", new[] { new FieldChange("MaxStackSize", "25") }),
            new BuildItem(pkg, new[] { new FieldChange("Weight", "0.5") }),
        });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        // Both edits land on the one copy — neither silently overwrites the other.
        Assert.Contains(writer.Applied, c => c.Field == "MaxStackSize" && c.NewValue == "25");
        Assert.Contains(writer.Applied, c => c.Field == "Weight" && c.NewValue == "0.5");
    }

    [Fact]
    public async Task An_added_field_hands_the_writer_the_donors_file()
    {
        var (builder, reader, writer, _, resolver, _) = New();
        const string target = "SCUM/Content/ConZ_Files/Items/Food/Tomato_ES";
        const string donor = "SCUM/Content/ConZ_Files/Items/Food/Stone_ES";
        AddPackage(reader, target);
        AddPackage(reader, donor);
        resolver.Map[target] = new AssetResolution(target, null);
        resolver.Map[donor] = new AssetResolution(donor, null);

        var change = new FieldChange("_autoDestructTime", "0", ChangeOperation.AddField, donor);
        var request = new BuildRequest("M", "1.0.0", new[] { new BuildItem(target, new[] { change }) });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.NotNull(writer.Donors);
        Assert.True(writer.Donors!.ContainsKey("_autoDestructTime"));
        Assert.EndsWith(".uasset", writer.Donors["_autoDestructTime"]);
    }

    [Fact]
    public async Task An_added_field_with_no_donor_leaves_the_writer_without_one()
    {
        var (builder, reader, writer, _, resolver, _) = New();
        const string target = "SCUM/Content/ConZ_Files/Items/Food/Tomato_ES";
        AddPackage(reader, target);
        resolver.Map[target] = new AssetResolution(target, null);

        var change = new FieldChange("_autoDestructTime", "0", ChangeOperation.AddField,
                                    "SCUM/Content/ConZ_Files/Items/Food/Ghost_ES");
        var request = new BuildRequest("M", "1.0.0", new[] { new BuildItem(target, new[] { change }) });

        await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.Null(writer.Donors);   // nothing to hand over; the writer will fail plainly
    }

    /// <summary>A tiny valid asset index carrying one recipe donor, so the writer can add to it.</summary>
    private static byte[] DonorRegistry(string donorName = "CR_1H_Stone_Axe")
    {
        const string cls = "ItemCraftingRecipe";
        const string pkg = "/Game/ConZ_Files/Items/Crafting/Recipes/Items";
        var reg = new FakeAssetRegistry();
        reg.Name("PrimaryAssetType");
        reg.Name("PrimaryAssetName");
        reg.Name("NativeClass");
        reg.NumberlessNames.Add((uint)reg.Name(cls));
        reg.Pairs.Add(((uint)reg.Name("PrimaryAssetType"), 2u));
        reg.Pairs.Add(((uint)reg.Name("PrimaryAssetName"), 2u));
        reg.Pairs.Add(((uint)reg.Name("NativeClass"), 2u));
        reg.AddRecord(new FakeRecord(new[]
        {
            $"{pkg}/{donorName}.{donorName}", pkg, cls, $"{pkg}/{donorName}", donorName,
        }, TagHandle: 0x8000000300000000UL, ChunkIds: new[] { 0 }));
        return reg.Build();
    }

    private const string DonorPath =
        "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";
    private const string NewFolder =
        "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_Test";

    private static NewBuildAsset NewRecipe(IReadOnlyList<FieldChange>? changes = null) =>
        new("CR_New", "CR_1H_Stone_Axe", DonorPath, NewFolder,
            changes ?? Array.Empty<FieldChange>());

    [Fact]
    public async Task A_new_asset_copies_the_donor_under_its_own_name_and_ships_the_index()
    {
        var (builder, reader, _, packer, _, cloner) = New();
        AddPackage(reader, DonorPath);
        reader.Index = DonorRegistry();

        var request = new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>(), new[] { NewRecipe() });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains(cloner.Calls, c => c.OldName == "CR_1H_Stone_Axe" && c.NewName == "CR_New");

        var staged = Path.Combine(packer.Staging!, NewFolder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(Path.Combine(staged, "CR_New.uasset")));
        Assert.True(File.Exists(Path.Combine(staged, "CR_New.uexp")));
        Assert.False(File.Exists(Path.Combine(staged, "CR_1H_Stone_Axe.uasset")));

        var index = Path.Combine(packer.Staging!, "SCUM", "AssetRegistry.bin");
        Assert.True(File.Exists(index));
        Assert.True(AssetRegistryReader.TryRead(File.ReadAllBytes(index), out var after, out _));
        Assert.Contains(after!.Assets, a => a.AssetName == "CR_New");
    }

    [Fact]
    public async Task A_new_assets_own_edits_are_written_onto_the_copy()
    {
        var (builder, reader, writer, _, _, _) = New();
        AddPackage(reader, DonorPath);
        reader.Index = DonorRegistry();
        var changes = new[] { new FieldChange("CraftingTimeSeconds", "5") };

        var request = new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>(),
            new[] { NewRecipe(changes) });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains(writer.Applied, c => c.Field == "CraftingTimeSeconds" && c.NewValue == "5");
    }

    [Fact]
    public async Task A_new_asset_with_a_missing_donor_fails_the_build_with_no_pak()
    {
        var (builder, _, _, packer, _, _) = New();
        // The donor package is never added, so it cannot be read.
        var request = new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>(), new[] { NewRecipe() });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(packer.Pak);
    }

    [Fact]
    public async Task A_cloner_that_cannot_rename_fails_the_build()
    {
        var (builder, reader, _, packer, _, cloner) = New();
        AddPackage(reader, DonorPath);
        reader.Index = DonorRegistry();
        cloner.Ok = false;

        var request = new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>(), new[] { NewRecipe() });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(packer.Pak);
    }

    [Fact]
    public async Task A_build_that_cannot_read_the_index_fails_with_no_pak()
    {
        var (builder, reader, _, packer, _, _) = New();
        AddPackage(reader, DonorPath);
        // No Raw entry for the index -> it cannot be read.

        var request = new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>(), new[] { NewRecipe() });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(packer.Pak);
    }

    [Fact]
    public async Task An_index_that_cannot_take_the_entry_fails_the_build_with_no_pak()
    {
        var (builder, reader, _, packer, _, _) = New();
        AddPackage(reader, DonorPath);
        reader.Index = new byte[] { 1, 2, 3 };   // not an asset index

        var request = new BuildRequest("M", "1.0.0", Array.Empty<BuildItem>(), new[] { NewRecipe() });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(packer.Pak);
    }

    private sealed class ThrowingReader : IPackageReader
    {
        public bool TryReadPackage(string path, out IReadOnlyDictionary<string, byte[]>? files)
            => throw new IOException("boom");
    }

    [Fact]
    public async Task An_unexpected_error_fails_the_build_plainly_and_never_throws()
    {
        var resolver = new FakeResolver();
        resolver.Map["X"] = new AssetResolution("SCUM/Content/ConZ_Files/Items/X_ES", null);
        var builder = new ModBuilder(new ThrowingReader(), new FakeWriter(), new FakePacker(),
            resolver, new FakeCloner(), Path.Combine(_root, "Builds"));
        var request = new BuildRequest("M", "1.0.0",
            new[] { new BuildItem("X", new[] { new FieldChange("F", "1") }) });

        var outcome = await builder.BuildAsync(request, null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Error));
    }
}
