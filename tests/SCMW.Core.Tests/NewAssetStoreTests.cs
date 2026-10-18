using SCMW.Core.Editing;

public class NewAssetStoreTests
{
    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-newassets-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static NewAsset Asset(
        Guid? id = null,
        string name = "CR_Test",
        NewAssetKind kind = NewAssetKind.Recipe,
        string donorName = "CR_1H_Stone_Axe",
        string donorPath = "ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe",
        string folder = "ConZ_Files/Items/Crafting/Recipes/Items/SCMW_Test",
        DateTime? createdUtc = null) =>
        new(id ?? Guid.NewGuid(), name, kind, donorName, donorPath, folder,
            createdUtc ?? new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));

    [Fact] // add -> load round-trip preserves every field, value equality included
    public void Add_then_load_round_trips_every_field()
    {
        var folder = TempFolder();
        INewAssetStore store = new NewAssetStore();
        var asset = Asset(kind: NewAssetKind.Recipe);

        Assert.True(store.Add(folder, asset));

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal(asset, loaded);
    }

    [Fact] // adding the same Id twice is idempotent: one asset, first write wins
    public void Adding_the_same_id_twice_keeps_one_asset()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        var id = Guid.NewGuid();

        store.Add(folder, Asset(id: id, name: "CR_First"));
        store.Add(folder, Asset(id: id, name: "CR_Second"));

        var loaded = Assert.Single(store.Load(folder));
        Assert.Equal("CR_First", loaded.Name);
    }

    [Fact] // undo removes exactly one asset and leaves the rest in order
    public void Undo_removes_exactly_one_asset_and_preserves_order()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        var first = Asset(name: "CR_A");
        var middle = Asset(name: "CR_B");
        var last = Asset(name: "CR_C");
        store.Add(folder, first);
        store.Add(folder, middle);
        store.Add(folder, last);

        store.Undo(folder, middle.Id);

        var remaining = store.Load(folder);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(new[] { first.Id, last.Id }, remaining.Select(a => a.Id).ToArray());
    }

    [Fact] // undoing an unknown id is a no-op, not a corruption
    public void Undo_of_an_unknown_id_changes_nothing()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        var asset = Asset();
        store.Add(folder, asset);

        store.Undo(folder, Guid.NewGuid());

        Assert.Equal(asset, Assert.Single(store.Load(folder)));
    }

    [Fact] // replace swaps the asset with the same Id in place, keeping its position
    public void Replace_swaps_the_asset_with_the_same_id_in_place()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        var first = Asset(name: "CR_A");
        var second = Asset(name: "CR_B");
        store.Add(folder, first);
        store.Add(folder, second);

        var renamed = first with { Name = "CR_A2" };
        Assert.True(store.Replace(folder, renamed));

        var loaded = store.Load(folder);
        Assert.Equal(new[] { first.Id, second.Id }, loaded.Select(a => a.Id).ToArray());
        Assert.Equal("CR_A2", loaded[0].Name);
    }

    [Fact] // a damaged new-assets.json is kept aside, never silently overwritten as empty
    public void A_damaged_asset_file_is_kept_aside()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, NewAssetStore.FileName), "{ this is not json");

        var store = new NewAssetStore();
        Assert.Empty(store.Load(folder));
        Assert.Empty(store.Load(folder));   // still empty on a second read

        Assert.Single(Directory.GetFiles(folder, "new-assets.corrupt-*.json"));
    }

    [Fact] // the file is camelCase and human-readable (enum as a name, not a number)
    public void Asset_file_is_camel_case()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        store.Add(folder, Asset(kind: NewAssetKind.Recipe));

        var json = File.ReadAllText(Path.Combine(folder, NewAssetStore.FileName));
        Assert.Contains("\"assets\"", json);
        Assert.Contains("\"donorName\"", json);
        Assert.Contains("\"donorPath\"", json);
        Assert.Contains("\"folder\"", json);
        Assert.DoesNotContain("\"DonorName\"", json);
        Assert.Contains("\"recipe\"", json);
    }

    [Fact] // two projects never see each other's new assets
    public void Two_projects_are_isolated()
    {
        var one = TempFolder();
        var two = TempFolder();
        var store = new NewAssetStore();

        store.Add(one, Asset(name: "CR_One"));

        Assert.Single(store.Load(one));
        Assert.Empty(store.Load(two));
        Assert.False(File.Exists(Path.Combine(two, NewAssetStore.FileName)));

        store.Add(two, Asset(name: "CR_Two"));

        Assert.Equal("CR_One", Assert.Single(store.Load(one)).Name);
        Assert.Equal("CR_Two", Assert.Single(store.Load(two)).Name);
    }

    [Fact] // a null/blank project folder is rejected up-front: never an unhandled exception
    public void A_null_or_blank_project_folder_never_throws()
    {
        INewAssetStore store = new NewAssetStore();
        var asset = Asset();

        foreach (string? folder in new string?[] { null, "", "   " })
        {
            Assert.Empty(store.Load(folder!));
            Assert.False(store.Add(folder!, asset));   // no-op, never throws
            store.Undo(folder!, asset.Id);               // no-op, never throws
            Assert.False(store.Replace(folder!, asset));
        }
    }

    [Fact] // adding a new asset tells listeners to re-read; a duplicate add changes nothing, so it stays quiet
    public void Adding_a_new_asset_raises_changed_but_a_duplicate_does_not()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        var raised = 0;
        store.Changed += () => raised++;

        var first = Asset(name: "CR_First");
        store.Add(folder, first);
        Assert.Equal(1, raised);

        store.Add(folder, first);                                   // same id => idempotent, no change
        Assert.Equal(1, raised);

        store.Add(folder, Asset(name: "CR_Other"));                 // a new id => a real change
        Assert.Equal(2, raised);
    }

    [Fact] // removing an asset tells listeners; removing an unknown id changes nothing, so it stays quiet
    public void Undoing_a_known_asset_raises_changed_and_an_unknown_one_does_not()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        var asset = Asset();
        store.Add(folder, asset);

        var raised = 0;
        store.Changed += () => raised++;

        store.Undo(folder, Guid.NewGuid());
        Assert.Equal(0, raised);

        store.Undo(folder, asset.Id);
        Assert.Equal(1, raised);
    }

    [Fact] // a blank project folder writes nothing, so listeners are never told to re-read
    public void A_blank_folder_never_raises_changed()
    {
        var store = new NewAssetStore();
        var raised = 0;
        store.Changed += () => raised++;

        store.Add("", Asset());
        store.Undo("", Guid.NewGuid());
        store.Replace("", Asset());

        Assert.Equal(0, raised);
    }

    [Fact] // atomicity: a failed write leaves the previous file exactly as it was, with no temp left
    public void A_failed_write_leaves_the_previous_file_intact()
    {
        var folder = TempFolder();
        var store = new NewAssetStore();
        store.Add(folder, Asset(name: "CR_First"));

        var path = Path.Combine(folder, NewAssetStore.FileName);
        var original = File.ReadAllText(path);

        // Force the atomic replace to fail: a read-only target cannot be overwritten.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(store.Add(folder, Asset(name: "CR_Second")));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Equal(original, File.ReadAllText(path));                 // previous list intact
        Assert.Equal("CR_First", Assert.Single(store.Load(folder)).Name);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));              // no temp file left behind
    }
}

public class NewAssetNamingTests
{
    [Fact] // the new asset's folder is the donor's folder plus a slug from the project name
    public void Folder_is_the_donor_folder_plus_a_project_slug()
    {
        var folder = NewAssetNaming.FolderFor("ConZ_Files/Items/Crafting/Recipes/Items", "Olive Press");

        Assert.Equal("ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePress", folder);
    }

    [Fact] // a trailing slash on the donor folder never doubles up
    public void Folder_ignores_a_trailing_slash_on_the_donor_folder()
    {
        var folder = NewAssetNaming.FolderFor("A/B/", "MyMod");

        Assert.Equal("A/B/SCMW_MyMod", folder);
    }

    [Fact] // the slug keeps only letters and digits, so it is safe as a folder name
    public void Slug_keeps_only_letters_and_digits()
    {
        Assert.Equal("MyFirstMod", NewAssetNaming.Slug("My First Mod!"));
        Assert.Equal("OlivePress", NewAssetNaming.Slug("Olive Press"));
    }

    [Fact] // a project name with nothing usable still yields a safe folder
    public void Slug_falls_back_when_the_name_has_nothing_usable()
    {
        Assert.Equal("Mod", NewAssetNaming.Slug("***"));
        Assert.Equal("ConZ_Files/Items/SCMW_Mod", NewAssetNaming.FolderFor("ConZ_Files/Items", "***"));
    }

    [Fact] // a plain name the owner typed becomes a valid recipe name: "CR_" + its letters and digits
    public void A_plain_name_becomes_a_CR_recipe_name()
    {
        Assert.Equal("CR_OlivePress", NewAssetNaming.RecipeName("Olive Press"));
        Assert.Equal("CR_olivepress", NewAssetNaming.RecipeName("  olive   press  ")); // case kept as typed
    }

    [Fact] // a name the owner typed with the prefix already is not doubled up
    public void A_name_that_already_starts_with_CR_does_not_double_up()
    {
        Assert.Equal("CR_OlivePress", NewAssetNaming.RecipeName("CR_Olive Press"));
        Assert.Equal("CR_olive", NewAssetNaming.RecipeName("cr-olive"));
    }

    [Fact] // a word that merely begins with "CR" keeps its letters (Crate must not lose "Cr")
    public void A_word_merely_starting_with_CR_keeps_its_letters()
    {
        Assert.Equal("CR_Crate", NewAssetNaming.RecipeName("Crate"));
    }

    [Fact] // nothing usable to name with means no recipe name at all, never a stray "CR_Mod"
    public void A_name_with_nothing_usable_has_no_recipe_name()
    {
        Assert.Null(NewAssetNaming.RecipeName(null));
        Assert.Null(NewAssetNaming.RecipeName(""));
        Assert.Null(NewAssetNaming.RecipeName("***"));
    }

    [Fact] // typing only the prefix is refused, never turned into the absurd "CR_CR"
    public void A_bare_CR_prefix_has_no_recipe_name()
    {
        Assert.Null(NewAssetNaming.RecipeName("CR"));
        Assert.Null(NewAssetNaming.RecipeName("CR_"));
        Assert.Null(NewAssetNaming.RecipeName("cr "));
    }

    [Fact] // a plain name the owner typed becomes an item's name (letters and digits only)
    public void A_plain_name_becomes_an_item_name()
    {
        Assert.Equal("OliveOil", NewAssetNaming.ItemName("Olive Oil"));
        Assert.Equal("CrudeOil2", NewAssetNaming.ItemName("Crude Oil 2"));
    }

    [Fact] // nothing usable to name with means no item name at all
    public void A_name_with_nothing_usable_has_no_item_name()
    {
        Assert.Null(NewAssetNaming.ItemName(null));
        Assert.Null(NewAssetNaming.ItemName("***"));
    }

    [Fact] // an ITEM is two files: the item and its "_ES" twin. The twin is what the owner's edits
           // are recorded against, so the record points its edit path at it.
    public void An_item_records_its_edit_path_at_its_es_twin()
    {
        var item = new NewAsset(Guid.NewGuid(), "OliveOil", NewAssetKind.Item, "VegetableOil",
            "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil",
            "SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePress",
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePress/OliveOil_ES",
            item.EditPath);
    }

    [Fact] // a recipe is one file, so its edit path is its own package path
    public void A_recipe_records_its_edit_path_at_its_own_package()
    {
        var recipe = new NewAsset(Guid.NewGuid(), "CR_OlivePress", NewAssetKind.Recipe, "CR_1H_Stone_Axe",
            "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe",
            "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePress",
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(recipe.PackagePath, recipe.EditPath);
    }

    [Fact] // the twin is the donor's "_ES" sibling, and the new twin is the new name's "_ES" sibling
    public void The_twin_is_the_donors_es_sibling()
    {
        var item = new NewAsset(Guid.NewGuid(), "OliveOil", NewAssetKind.Item, "VegetableOil",
            "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil",
            "SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePress",
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil_ES", item.TwinDonorPath);
        Assert.Equal("SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePress/OliveOil_ES",
            item.TwinPackagePath);
        Assert.Equal("OliveOil_ES", item.TwinName);
    }

    [Fact] // a recipe has no twin (a recipe is a single file)
    public void A_recipe_has_no_twin()
    {
        var recipe = new NewAsset(Guid.NewGuid(), "CR_OlivePress", NewAssetKind.Recipe, "CR_1H_Stone_Axe",
            "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe",
            "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePress",
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));

        Assert.Null(recipe.TwinDonorPath);
        Assert.Null(recipe.TwinPackagePath);
        Assert.Null(recipe.TwinName);
    }

    [Fact] // the package path the build and the field edits aim at is the folder plus the name
    public void Package_path_is_the_folder_plus_the_name()
    {
        var asset = new NewAsset(Guid.NewGuid(), "CR_OlivePress", NewAssetKind.Recipe,
            "CR_1H_Stone_Axe", "ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe",
            "ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePress",
            new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePress/CR_OlivePress",
            asset.PackagePath);
    }
}
