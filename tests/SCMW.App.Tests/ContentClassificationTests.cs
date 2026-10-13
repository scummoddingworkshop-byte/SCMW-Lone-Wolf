using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

/// <summary>
/// Real SCUM assets carry classification in their asset name: an "item" ends in _ES
/// (the editable copy), a non-item is a recipe (CR_), ingredient (CI_), icon (ICO_) or
/// chopping entry (Chop_). The catalog must carry that onto the screen.
/// </summary>
public class ContentClassificationTests
{
    private static readonly IContentSource NoSource = new NullSource();

    private static AssetSummary Asset(string id) =>
        new(id, id, "Equipment", StatusKind.Unknown, 0, id + ".uasset", null);

    [AvaloniaFact]
    public void An_es_item_is_flagged_editable()
    {
        var catalog = new ContentCatalog(NoSource);

        var items = catalog.MapItems(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Equipment/Active_Items/Feather_ES"),
        });

        Assert.True(Assert.Single(items).Editable);
    }

    [AvaloniaFact]
    public void A_plain_item_is_not_flagged_editable()
    {
        var catalog = new ContentCatalog(NoSource);

        var items = catalog.MapItems(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Equipment/Batteries"),
        });

        Assert.False(Assert.Single(items).Editable);
    }

    [AvaloniaFact]
    public void Non_items_are_filtered_out()
    {
        var catalog = new ContentCatalog(NoSource);

        var items = catalog.MapItems(new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES"),
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Wooden_Arrow_Feathers"),
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Feather"),
            Asset("SCUM/Content/ConZ_Files/Items/BrokenArrows/ICO_Arrow_Wooden_Feathers_Broken"),
            Asset("SCUM/Content/ConZ_Files/Items/Crafting/Chopping/Chop_Items/Chop_Car_Battery"),
        });

        var item = Assert.Single(items);
        Assert.Equal("Batteries_ES", item.AssetId!.Split('/')[^1]);
    }

    [AvaloniaFact]
    public void Base_elements_are_their_own_family()
    {
        var catalog = new ContentCatalog(NoSource);
        var assets = new[]
        {
            Asset("SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES"),
            Asset("SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well"),
        };

        // Items list stays item-only; base elements get their own list with a friendly name.
        var item = Assert.Single(catalog.MapItems(assets));
        Assert.EndsWith("Batteries_ES", item.AssetId);

        var baseElement = Assert.Single(catalog.MapBaseElements(assets));
        Assert.Equal("Well (base)", baseElement.Name);
        Assert.True(baseElement.Editable);
    }

    [AvaloniaFact]
    public void An_es_items_fields_are_editable_and_a_plain_items_are_not()
    {
        var catalog = new ContentCatalog(NoSource);
        var fields = new[] { new AssetField("Weight", "Weight", StatusKind.ReadOnly, "1.0", "FloatProperty") };

        var esValues = catalog.MapFields(fields, editableItem: true);
        Assert.True(Assert.Single(esValues).Editable);

        var plainValues = catalog.MapFields(fields, editableItem: false);
        Assert.False(Assert.Single(plainValues).Editable);
    }

    [AvaloniaFact]
    public void A_structural_field_is_never_offered_even_on_an_es_item()
    {
        var catalog = new ContentCatalog(NoSource);
        var fields = new[] { new AssetField("Components", "Components", StatusKind.ReadOnly, "x", "ArrayProperty") };

        Assert.False(Assert.Single(catalog.MapFields(fields, editableItem: true)).Editable);
    }

    [AvaloniaFact]
    public async Task Only_es_items_hides_plain_items_and_nothing_else_helps()
    {
        var source = new TwoItemSource();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));
        await vm.LoadFromSourceAsync();

        // Default: only _ES (editable) items.
        Assert.True(vm.OnlyEditable);
        Assert.Contains(vm.VisibleItems, i => i.AssetId!.EndsWith("Batteries_ES"));
        Assert.DoesNotContain(vm.VisibleItems, i => i.AssetId!.EndsWith("/Batteries"));

        // Unticking shows the plain ones too.
        vm.OnlyEditable = false;
        Assert.Contains(vm.VisibleItems, i => i.AssetId!.EndsWith("/Batteries"));
    }

    /// <summary>One _ES item and one plain item; no game, no disk.</summary>
    private sealed class TwoItemSource : IContentSource
    {
        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Equipment" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(new[]
            {
                Asset("SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES"),
                Asset("SCUM/Content/ConZ_Files/Items/Equipment/Batteries"),
            });
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A do-nothing source for mapping tests; no game, no disk.</summary>
    private sealed class NullSource : IContentSource
    {
        public ContentSourceStatus Status => new(ContentSourceState.NotConfigured, "", null);
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => Array.Empty<string>();
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(Array.Empty<AssetSummary>());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
