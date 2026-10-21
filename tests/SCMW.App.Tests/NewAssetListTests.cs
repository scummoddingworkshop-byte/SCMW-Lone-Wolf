using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// A project's own new items and recipes must stay in the lists after a reload — not only in the
/// moment they are made. Items were missing entirely; the row also needs the friendly name the owner
/// typed ("Olive Oil", not "OliveOil") so it can be searched for.
/// </summary>
public class NewAssetListTests
{
    private const string Apple = "SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES";
    private const string OilDonor = "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil";
    private const string RecipeDonor = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";
    private const string CategoryDonor =
        "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Group_Canned_Vegetables";
    private const string Folder = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OliveMod";

    [AvaloniaFact]
    public async Task A_projects_new_item_stays_in_the_items_list()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            AddProjectAssets(projects);
            var vm = await Open(projects, "Items");

            var row = vm.VisibleAssets.Single(a => a.AssetId!.EndsWith("/OliveOil_ES"));
            Assert.Equal("Olive Oil", row.Name);                 // the friendly name, not "OliveOil"
            Assert.EndsWith("/VegetableOil_ES", row.ReadPath);   // reads the donor's values
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task New_recipes_show_but_new_items_and_categories_do_not_appear_as_recipes()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            AddProjectAssets(projects);
            var vm = await Open(projects, "Recipes");

            Assert.Contains(vm.VisibleAssets, a => a.AssetId!.EndsWith("/CR_OliveOil"));
            Assert.DoesNotContain(vm.VisibleAssets, a => a.AssetId!.EndsWith("/OliveOil_ES"));
            Assert.DoesNotContain(vm.VisibleAssets, a => a.AssetId!.EndsWith("/CI_Olives"));
        }
        finally { TestProjects.Cleanup(root); }
    }

    private static void AddProjectAssets(ProjectStore projects)
    {
        var store = new NewAssetStore();
        var folder = projects.Current!.FolderPath;
        var now = DateTime.UtcNow;

        store.Add(folder, new NewAsset(Guid.NewGuid(), "OliveOil", NewAssetKind.Item, "VegetableOil",
            OilDonor, Folder, now));
        store.Add(folder, new NewAsset(Guid.NewGuid(), "CR_OliveOil", NewAssetKind.Recipe, "1H Stone Axe",
            RecipeDonor, Folder, now));
        store.Add(folder, new NewAsset(Guid.NewGuid(), "CI_Olives", NewAssetKind.Category,
            "Group Canned Vegetables", CategoryDonor, Folder, now));
    }

    private static async System.Threading.Tasks.Task<WorkspaceViewModel> Open(ProjectStore projects,
                                                                              string kind)
    {
        var source = new ItemSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source), TestProjects.ChangeStore(projects),
                                        projects, new NewAssetStore());
        await vm.OpenKindAsync(kind);
        return vm;
    }

    /// <summary>A fake game index with one ordinary item; the rest come from the project's records.</summary>
    private sealed class ItemSource : IContentSource
    {
        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(Apple, "Apple", "Food", StatusKind.Editable, 1, Apple + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Food" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100,
                                                              CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(
                s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
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
