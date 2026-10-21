using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The item page's "Name &amp; description" card: change the words the game shows and copy a picture
/// from another item. These are ordinary Set records; the build writes them onto the item's "_ES" file.
/// </summary>
public class ItemIdentityEditorTests
{
    private const string OliveOil = "SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OliveMod/OliveOil_ES";
    private const string Olives = "SCUM/Content/ConZ_Files/Items/Food/Fruit/Olives_ES";
    private const string VegetableIcon =
        "/Game/ConZ_Files/Models/Objects/Items/Crafting_Items/VegetableOil/ICO_Vegetable_Oil.ICO_Vegetable_Oil";
    private const string OliveIcon =
        "/Game/ConZ_Files/Models/Objects/Items/Crafting_Items/Olives/ICO_Olives.ICO_Olives";

    private static AssetField F(string name, string value, string hint) =>
        new(name, name, StatusKind.ReadOnly, value, hint);

    private static IReadOnlyList<AssetField> OilFields() => new[]
    {
        F("Caption", "Vegetable Oil", "TextProperty"),
        F("Description", "A vegetable oil is a triglyceride extracted from a plant.", "TextProperty"),
        F("GeneralPurposeIcon", VegetableIcon, "SoftObjectProperty"),
        F("GridInventoryIcon", VegetableIcon, "SoftObjectProperty"),
        F("HandsAndShouldersIcon", VegetableIcon, "SoftObjectProperty"),
        F("Weight", "0", "FloatProperty"),
    };

    private static IReadOnlyList<AssetField> OliveFields() => new[]
    {
        F("Caption", "Olives", "TextProperty"),
        F("Description", "Fresh olives.", "TextProperty"),
        F("GeneralPurposeIcon", OliveIcon, "SoftObjectProperty"),
        F("GridInventoryIcon", OliveIcon, "SoftObjectProperty"),
        F("HandsAndShouldersIcon", OliveIcon, "SoftObjectProperty"),
    };

    [Fact]
    public void The_card_shows_the_names_the_game_has_now()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Olive Oil", OilFields(), assetPath: OliveOil);

        Assert.True(vm.ShowItemIdentity);
        Assert.True(vm.HasItemDescription);
        Assert.Equal("Vegetable Oil", vm.ItemNameDraft);
        Assert.StartsWith("A vegetable oil", vm.ItemDescriptionDraft);
    }

    [Fact]
    public void A_recipe_has_no_name_card()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Oil recipe", new[] { F("Caption", "Oil", "TextProperty") },
                assetPath: "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Oil");

        Assert.False(vm.ShowItemIdentity);
    }

    [AvaloniaFact]
    public async Task Saving_records_the_new_name_and_description()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await Opened(projects, OliveOil);

            vm.ItemNameDraft = "Olive Oil";
            vm.ItemDescriptionDraft = "Oil pressed from olives.";
            vm.SaveItemIdentityCommand.Execute(null);

            var records = TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath);
            Assert.Equal(2, records.Count);
            Assert.Contains(records, r => r.FieldTechnicalName == "Caption" && r.NewValue == "Olive Oil");
            Assert.Contains(records, r => r.FieldTechnicalName == "Description"
                                          && r.NewValue == "Oil pressed from olives.");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task An_empty_name_is_refused()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await Opened(projects, OliveOil);

            vm.ItemNameDraft = "   ";
            vm.SaveItemIdentityCommand.Execute(null);

            Assert.True(vm.HasIdentityNotice);
            Assert.Empty(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Using_a_picture_copies_all_three_links()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await Opened(projects, OliveOil);

            vm.PictureSource = vm.PictureSources.Single(p => p.Path == Olives);
            await vm.UsePictureCommand.ExecuteAsync(null);

            var records = TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath);
            Assert.Equal(3, records.Count);
            Assert.All(records, r => Assert.Equal(OliveIcon, r.NewValue));
            Assert.All(new[] { "GeneralPurposeIcon", "GridInventoryIcon", "HandsAndShouldersIcon" },
                icon => Assert.Contains(records, r => r.FieldTechnicalName == icon));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_card_renders_with_its_picture_picker()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var (vm, _) = await Opened(projects, OliveOil);
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 800 };
            window.Show();
            window.UpdateLayout();

            Assert.NotNull(view.FindControl<Border>("ItemIdentityCard"));
            Assert.NotNull(view.FindControl<ContentControl>("PictureSourceHost")!.Content);
        }
        finally { TestProjects.Cleanup(root); }
    }

    private static async System.Threading.Tasks.Task<(WorkspaceViewModel Vm, ItemSource Source)> Opened(
        ProjectStore projects, string assetId)
    {
        var source = new ItemSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source), TestProjects.ChangeStore(projects),
                                        projects, new NewAssetStore());
        await vm.OpenKindAsync("Items");
        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.AssetId == assetId));
        return (vm, source);
    }

    /// <summary>A fake item list: an oil item and the fresh olives whose picture it may borrow.</summary>
    private sealed class ItemSource : IContentSource
    {
        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(OliveOil, "Olive Oil", "Food", StatusKind.Editable, 1, OliveOil + ".uasset", null),
            new AssetSummary(Olives, "Olives", "Food", StatusKind.Editable, 1, Olives + ".uasset", null),
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
            Task.FromResult(id == Olives ? OliveFields() : OilFields());
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
