using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The adapting Workspace: one screen that shows a file's real fields, each explained
/// (label, unit, and a risk dot) from the field library.
/// </summary>
public class WorkspaceViewTests
{
    private static AssetField Field(string technical, string value) =>
        new(technical, technical, StatusKind.Editable, value);

    private static WorkspaceView Shown(out WorkspaceViewModel vm)
    {
        vm = new WorkspaceViewModel();
        vm.Show("Items", "Tomato", new[] { Field("Weight", "20000"), Field("MaxQuantity", "10") });
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void Renders_one_row_per_real_field_of_the_file()
    {
        var view = Shown(out _);

        var rows = view.GetVisualDescendants().OfType<FieldRow>().ToList();

        Assert.Equal(2, rows.Count);
    }

    [AvaloniaFact]
    public void A_row_shows_the_library_label_value_and_unit()
    {
        var view = Shown(out _);

        var weight = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => r.FindControl<TextBlock>("LabelText")!.Text == "Weight");

        Assert.Equal("20000", weight.FindControl<TextBlock>("ValueText")!.Text);
        Assert.Equal("grams", weight.FindControl<TextBlock>("UnitText")!.Text);
    }

    [AvaloniaFact]
    public void A_row_carries_the_fields_risk_for_its_dot()
    {
        var view = Shown(out _);

        var weight = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => r.FindControl<TextBlock>("LabelText")!.Text == "Weight");

        Assert.Equal(Risk.Low, ((WorkspaceField)weight.DataContext!).Risk);
        Assert.Contains("low", weight.FindControl<Border>("RiskDot")!.Classes);
    }

    [AvaloniaFact]
    public void The_library_label_wins_over_the_raw_technical_name()
    {
        var view = Shown(out _);

        var maxStack = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => ((WorkspaceField)r.DataContext!).TechnicalName == "MaxQuantity");

        Assert.Equal("Stack size", maxStack.FindControl<TextBlock>("LabelText")!.Text);
    }

    [AvaloniaFact]
    public void The_title_names_the_opened_file()
    {
        var view = Shown(out _);

        var titles = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("Tomato", titles);
    }

    [AvaloniaFact]
    public void A_field_the_library_does_not_describe_is_shown_honestly()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Odd thing", new[] { Field("SomeMysteryNumber", "42") });
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 300 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<FieldRow>().Single();
        var field = (WorkspaceField)row.DataContext!;

        Assert.False(field.Described);
        Assert.Equal(Risk.Unknown, field.Risk);
        Assert.True(row.IsVisible);                                  // never hidden
        Assert.Contains("unknown", row.FindControl<Border>("RiskDot")!.Classes);
        Assert.Equal(WorkspaceField.NotDescribed,
            row.FindControl<TextBlock>("MeaningText")!.Text);
        Assert.True(row.FindControl<TextBlock>("MeaningText")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_described_field_shows_its_plain_meaning_not_the_note()
    {
        var view = Shown(out _);

        var weight = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => ((WorkspaceField)r.DataContext!).TechnicalName == "Weight");
        var field = (WorkspaceField)weight.DataContext!;
        var meaning = weight.FindControl<TextBlock>("MeaningText")!;

        Assert.True(field.Described);
        Assert.Equal(field.Meaning, meaning.Text);
        Assert.NotEqual(WorkspaceField.NotDescribed, meaning.Text);
    }

    // --- Search + browse (Task 2.5) -------------------------------------------------------------

    private static WorkspaceViewModel Browsing()
    {
        var vm = new WorkspaceViewModel();
        vm.Browse("Items", new[]
        {
            new ItemSummary("Tomato", "Food", "", editable: true),
            new ItemSummary("Tomato (unripe)", "Food", "", editable: false),
            new ItemSummary("Fire Axe", "Weapons", "", editable: true),
        });
        return vm;
    }

    [AvaloniaFact]
    public void Browsing_offers_the_games_categories_as_chips()
    {
        var vm = Browsing();

        Assert.Contains("All", vm.Categories);
        Assert.Contains("Food", vm.Categories);
        Assert.Contains("Weapons", vm.Categories);
    }

    [AvaloniaFact]
    public void The_only_editable_tick_hides_rows_the_writer_cannot_change()
    {
        var vm = Browsing();

        Assert.True(vm.OnlyEditable);
        Assert.DoesNotContain(vm.VisibleAssets, a => !a.Editable);

        vm.OnlyEditable = false;
        Assert.Contains(vm.VisibleAssets, a => a.Name == "Tomato (unripe)");
    }

    [AvaloniaFact]
    public void A_category_chip_narrows_the_browse_list()
    {
        var vm = Browsing();

        vm.SelectCategoryCommand.Execute("Food");

        Assert.Equal("Food", vm.SelectedCategory);
        Assert.All(vm.VisibleAssets, a => Assert.Equal("Food", a.Category));
    }

    [AvaloniaFact]
    public void Search_filters_the_browse_list_by_name()
    {
        var vm = Browsing();

        vm.Search = "axe";

        var only = Assert.Single(vm.VisibleAssets);
        Assert.Equal("Fire Axe", only.Name);
    }

    [AvaloniaFact]
    public void Browsing_renders_the_rows_that_pass_the_filters()
    {
        var vm = Browsing();
        vm.Search = "axe";
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        window.Show();

        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "AssetList");

        Assert.True(list.IsVisible);
        Assert.Single(list.GetVisualDescendants().OfType<ItemRow>());
    }

    // --- Per-file truth (Task 2.7, Review Focus 2) ----------------------------------------------

    [AvaloniaFact]
    public async Task Two_files_of_the_same_kind_show_their_own_fields()
    {
        var vm = new WorkspaceViewModel(source: new PerFileSource());
        vm.Browse("Items", new[]
        {
            new ItemSummary("Tomato", "Food", "", editable: true, assetId: "tomato-id"),
            new ItemSummary("Canned Beans", "Food", "", editable: true, assetId: "beans-id"),
        });

        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Tomato"));
        var tomato = vm.Fields.Select(f => f.TechnicalName).ToList();

        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Canned Beans"));
        var beans = vm.Fields.Select(f => f.TechnicalName).ToList();

        Assert.Equal(new[] { "Weight", "MaxQuantity" }, tomato);
        Assert.Equal(new[] { "Weight", "MaxQuantity", "SpoilTime" }, beans);
        Assert.NotEqual(tomato, beans);
        Assert.Equal("Canned Beans", vm.SelectedAsset!.Name);
    }

    // --- Loot & spawn family (Task 2.6) ---------------------------------------------------------

    [AvaloniaFact]
    public async Task A_spawner_preset_opens_in_the_workspace_and_its_fields_render()
    {
        const string presetId =
            "SCUM/Content/ConZ_Files/Items/SpawnerPresets2/Buildings/Warehouse/Examine_Crate_Brown_Big";

        var source = new SpawnerSource();
        var catalog = new ContentCatalog(source);
        var assets = catalog.MapSpawnerPresets(new[]
        {
            new AssetSummary(presetId, "Examine Crate Brown Big", "Buildings", StatusKind.Unknown, 0,
                presetId + ".uasset", null),
        });

        var vm = new WorkspaceViewModel(source: source);
        vm.Browse(AssetFamily.LootAndSpawn.ToString(), assets);
        await vm.OpenAsync(vm.VisibleAssets.Single());

        Assert.Equal("Examine Crate Brown Big", vm.SelectedAsset!.Name);

        var fields = vm.Fields.Select(f => f.TechnicalName).ToList();
        Assert.Contains("SpawnQuantity", fields);
        Assert.Contains("SpawnRadius", fields);

        // No library entries for this family yet: an unknown field still shows, honestly.
        Assert.Contains(vm.Fields, f => !f.Described);
    }

    // --- The kind picker + making the Workspace reachable (Task 3.0) ----------------------------

    [AvaloniaFact]
    public void The_kind_picker_offers_only_the_kinds_we_can_browse()
    {
        var vm = new WorkspaceViewModel();

        Assert.Equal(new[] { "Items", "Base building", "Recipes" }, vm.Kinds);
        Assert.DoesNotContain("Loot & spawn", vm.Kinds);
    }

    [AvaloniaFact]
    public void The_workspace_opens_on_items()
        => Assert.Equal("Items", new WorkspaceViewModel().SelectedKind);

    [AvaloniaFact]
    public async Task Picking_a_kind_loads_that_kinds_real_files()
    {
        var source = new BrowseSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source));

        await vm.OpenKindAsync("Recipes");

        Assert.Contains(vm.VisibleAssets, a => a.Name == "Wooden Arrow");
        Assert.DoesNotContain(vm.VisibleAssets, a => a.Name == "Batteries");
    }

    [AvaloniaFact]
    public void Clicking_a_file_row_opens_its_fields()
    {
        var source = new BrowseSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source));
        vm.Browse("Items", new[]
        {
            new ItemSummary("Batteries", "Equipment", "", editable: true, assetId: "batteries-id"),
        });

        vm.SelectedAsset = vm.VisibleAssets.Single();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Batteries", vm.Title);
        Assert.Contains(vm.Fields, f => f.TechnicalName == "Weight");
    }

    /// <summary>A fake game index: one _ES item and one recipe; reads a Weight field. No game, no disk.</summary>
    private sealed class BrowseSource : IContentSource
    {
        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary("SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES", "Batteries",
                "Equipment", StatusKind.Editable, 1,
                "SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES.uasset", null),
            new AssetSummary("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Wooden_Arrow",
                "Wooden Arrow", "Items", StatusKind.Editable, 1,
                "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Wooden_Arrow.uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Equipment", "Items" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(new[] { Field("Weight", "400") });
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A fake source that reads one spawner preset's fields. No game, no disk.</summary>
    private sealed class SpawnerSource : IContentSource
    {
        private const string Id =
            "SCUM/Content/ConZ_Files/Items/SpawnerPresets2/Buildings/Warehouse/Examine_Crate_Brown_Big";

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Buildings" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(Array.Empty<AssetSummary>());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(id == Id
                ? new[] { Field("SpawnQuantity", "3"), Field("SpawnRadius", "500") }
                : Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A fake source: two files of one kind, each with its own fields. No game, no disk.</summary>
    private sealed class PerFileSource : IContentSource
    {
        private readonly Dictionary<string, IReadOnlyList<AssetField>> _fields = new()
        {
            ["tomato-id"] = new[] { Field("Weight", "20000"), Field("MaxQuantity", "10") },
            ["beans-id"] = new[] { Field("Weight", "400"), Field("MaxQuantity", "12"), Field("SpoilTime", "7200") },
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Food" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(Array.Empty<AssetSummary>());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_fields.TryGetValue(id, out var f) ? f : Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
