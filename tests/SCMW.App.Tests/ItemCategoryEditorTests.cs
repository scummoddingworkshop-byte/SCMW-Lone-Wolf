using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The item page's "Ingredient category" card: show what an item counts as, add/remove a category,
/// and create a brand-new category from a game one. These are the records the build later writes.
/// </summary>
public class ItemCategoryEditorTests
{
    private const string Olives = "SCUM/Content/ConZ_Files/Items/Food/Fruit/Olives_ES";
    private const string CannedOlives = "SCUM/Content/ConZ_Files/Items/Food/Canned_Food/BlackOlives_ES";
    private const string CannedVegetables =
        "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Group_Canned_Vegetables";

    private static AssetField Field(string technical, string value, string hint = "FloatProperty") =>
        new(technical, technical, StatusKind.ReadOnly, value, hint, Nested: false);

    private static AssetField Nested(string technical, string value, string hint) =>
        new(technical, technical, StatusKind.ReadOnly, value, hint, Nested: true);

    private static IReadOnlyList<AssetField> PlainOlives() => new[]
    {
        Field("Weight", "0.2"),
    };

    private static IReadOnlyList<AssetField> CannedOlivesFields() => new[]
    {
        Field("CraftingIngredientInstances", "StructProperty[1]", "ArrayProperty"),
        Nested("CraftingIngredientInstances[0].Type", "CI_Group_Canned_Vegetables", "ObjectProperty"),
        Nested("CraftingIngredientInstances[0].Amount", "1", "FloatProperty"),
        Field("Weight", "0.5"),
    };

    [Fact]
    public void An_item_shows_a_row_for_each_category_it_counts_as()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Black Olives", CannedOlivesFields(), assetPath: CannedOlives);

        Assert.True(vm.ShowItemCategories);
        var row = Assert.Single(vm.ItemCategoryRows);
        Assert.Equal("Group Canned Vegetables", row.Label);
        Assert.Equal("CI_Group_Canned_Vegetables", row.TagName);
        Assert.Equal("1", row.Amount);
    }

    [Fact]
    public void An_item_with_no_category_says_so()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Olives", PlainOlives(), assetPath: Olives);

        Assert.True(vm.ShowItemCategories);
        Assert.True(vm.HasNoItemCategories);
        Assert.Empty(vm.ItemCategoryRows);
    }

    [Fact]
    public void A_recipe_has_no_category_card()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Olive Press", new[] { Field("Caption", "Olive Press") },
                assetPath: "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_OlivePress");

        Assert.False(vm.ShowItemCategories);
        Assert.Empty(vm.ItemCategoryRows);
    }

    [AvaloniaFact]
    public async Task Adding_a_first_category_records_the_whole_list_from_a_donor()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await OlivesOpened(projects);

            vm.NewItemCategory = new CategoryChoice("CI_Olives", "Olives",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod/CI_Olives");
            vm.AddItemCategoryCommand.Execute(null);

            var folder = projects.Chosen!.FolderPath;
            var record = Assert.Single(Records(projects, ChangeOperation.AddField));
            Assert.Equal(ItemCategories.ListName, record.FieldTechnicalName);
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod/CI_Olives",
                         record.NewValue);
            Assert.Equal(CannedOlives, record.DonorPath);
            Assert.Equal(Olives, record.AssetPath);

            var row = Assert.Single(vm.ItemCategoryRows);
            Assert.Equal("Olives", row.Label);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_the_only_added_category_drops_the_whole_list_change()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await OlivesOpened(projects);

            vm.NewItemCategory = new CategoryChoice("CI_Olives", "Olives",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod/CI_Olives");
            vm.AddItemCategoryCommand.Execute(null);
            vm.RemoveItemCategoryCommand.Execute(vm.ItemCategoryRows.Single());

            var folder = projects.Chosen!.FolderPath;
            Assert.Empty(Records(projects, ChangeOperation.AddField));
            Assert.True(vm.HasNoItemCategories);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_second_category_is_appended_and_pointed_at_its_own_file()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await CannedOlivesOpened(projects);

            vm.NewItemCategory = new CategoryChoice("CI_Olives", "Olives",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod/CI_Olives");
            vm.AddItemCategoryCommand.Execute(null);

            var folder = projects.Chosen!.FolderPath;
            Assert.Single(Records(projects, ChangeOperation.AddEntry));

            var set = Assert.Single(Records(projects, ChangeOperation.Set));
            Assert.Equal(ItemCategories.TagPath(1), set.FieldTechnicalName);
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod/CI_Olives",
                         set.NewValue);

            Assert.Equal(2, vm.ItemCategoryRows.Count);
            Assert.Equal("Olives", vm.ItemCategoryRows[1].Label);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Making_a_new_category_records_it_and_gives_it_to_the_item()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, source) = await OlivesOpened(projects);

            vm.NewCategoryName = "Olives";
            vm.NewCategoryDonor = vm.CategoryDonors.Single(c => c.Name == "CI_Group_Canned_Vegetables");
            vm.CreateItemCategoryCommand.Execute(null);

            var folder = projects.Chosen!.FolderPath;
            var category = Assert.Single(new NewAssetStore().Load(folder)
                .Where(a => a.Kind == NewAssetKind.Category));
            Assert.Equal("CI_Olives", category.Name);
            Assert.Equal(CannedVegetables, category.DonorPath);
            Assert.EndsWith("/SCMW_OliveMod/CI_Olives", category.PackagePath);

            var record = Assert.Single(Records(projects, ChangeOperation.AddField));
            Assert.Equal(category.PackagePath, record.NewValue);
            Assert.Equal("CI_Olives", vm.ItemCategoryRows.Single().TagName);

            // The new category is offered again for any item.
            Assert.Contains(vm.CategoryChoices, c => c.Name == "CI_Olives");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_card_renders_with_its_two_dropdowns()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await OlivesOpened(projects);
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 700 };
            window.Show();
            window.UpdateLayout();

            var card = view.FindControl<Border>("ItemCategoryCard");
            Assert.NotNull(card);
            Assert.True(card!.IsVisible);

            // The dropdowns are built in code (the app's popup quirk), not declared in the markup.
            Assert.NotNull(view.FindControl<ContentControl>("ItemCategoryPickerHost")!.Content);
            Assert.NotNull(view.FindControl<ContentControl>("CategoryDonorPickerHost")!.Content);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Two_categories_on_a_fresh_item_survive_removing_either_one()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await OlivesOpened(projects);
            const string folderPath = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod";

            vm.NewItemCategory = new CategoryChoice("CI_Olives", "Olives", folderPath + "/CI_Olives");
            vm.AddItemCategoryCommand.Execute(null);
            vm.NewItemCategory = new CategoryChoice("CI_Fruit", "Fruit", folderPath + "/CI_Fruit");
            vm.AddItemCategoryCommand.Execute(null);

            Assert.Equal(new[] { "CI_Olives", "CI_Fruit" }, vm.ItemCategoryRows.Select(r => r.TagName));

            vm.RemoveItemCategoryCommand.Execute(vm.ItemCategoryRows[0]);

            var left = Assert.Single(vm.ItemCategoryRows);
            Assert.Equal("CI_Fruit", left.TagName);
            var record = Assert.Single(Records(projects, ChangeOperation.AddField));
            Assert.Equal(folderPath + "/CI_Fruit", record.NewValue);

            vm.RemoveItemCategoryCommand.Execute(vm.ItemCategoryRows[0]);
            Assert.Empty(Records(projects, ChangeOperation.AddField));
            Assert.True(vm.HasNoItemCategories);
        }
        finally { TestProjects.Cleanup(root); }
    }

    private static IReadOnlyList<ChangeRecord> Records(ProjectStore projects, ChangeOperation operation) =>
        TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath)
            .Where(r => r.Operation == operation).ToList();

    private static async System.Threading.Tasks.Task<(WorkspaceViewModel Vm, ItemSource Source)> OlivesOpened(
        ProjectStore projects)
        => await Opened(projects, Olives);

    private static async System.Threading.Tasks.Task<(WorkspaceViewModel Vm, ItemSource Source)> CannedOlivesOpened(
        ProjectStore projects)
        => await Opened(projects, CannedOlives);

    private static async System.Threading.Tasks.Task<(WorkspaceViewModel Vm, ItemSource Source)> Opened(
        ProjectStore projects, string assetId)
    {
        var source = new ItemSource();
        var changes = TestProjects.ChangeStore(projects);
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source), changes, projects,
                                        new NewAssetStore());
        await vm.OpenKindAsync("Items");
        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.AssetId == assetId));
        return (vm, source);
    }

    /// <summary>A fake game index: two items, one with a category, plus the category asset itself.</summary>
    private sealed class ItemSource : IContentSource
    {
        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(Olives, "Olives", "Food", StatusKind.Editable, 1, Olives + ".uasset", null),
            new AssetSummary(CannedOlives, "Black Olives", "Food", StatusKind.Editable, 1,
                             CannedOlives + ".uasset", null),
            new AssetSummary(CannedVegetables, "CI Group Canned Vegetables", "Crafting",
                             StatusKind.Unknown, 0, CannedVegetables + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Food", "Crafting" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100,
                                                              CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(
                s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(id switch
            {
                CannedOlives => CannedOlivesFields(),
                Olives => PlainOlives(),
                _ => (IReadOnlyList<AssetField>)Array.Empty<AssetField>(),
            });
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null,
                                                                  CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids,
                                                               IProgress<IconExtractionProgress>? p = null,
                                                               CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}


