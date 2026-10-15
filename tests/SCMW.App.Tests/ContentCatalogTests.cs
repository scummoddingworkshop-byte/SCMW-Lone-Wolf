using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

public class ContentCatalogTests
{
    [AvaloniaFact]
    public void An_asset_becomes_an_item_row()
    {
        var catalog = new ContentCatalog(new NullSource());
        var items = catalog.MapItems(new[]
        {
            new AssetSummary("SCUM/.../Items/Weapons/Ranged_Weapons/Improvised_Bow_ES", "Improvised Bow ES",
                "Weapons", StatusKind.Unknown, 0, "SCUM/.../Improvised_Bow_ES.uasset", null),
        });

        var item = Assert.Single(items);
        Assert.Equal("Improvised Bow", item.Name);   // the trailing "ES" is dropped
        Assert.Equal("Weapons", item.Category);
    }

    [AvaloniaFact]
    public void A_recipe_keeps_the_index_name_so_the_build_can_find_it()
    {
        var catalog = new ContentCatalog(new NullSource());

        var recipes = catalog.MapRecipes(new[]
        {
            new AssetSummary(
                "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden",
                "Base Garden", "Crafting", StatusKind.Unknown, 0,
                "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden.uasset", null),
        });

        var recipe = Assert.Single(recipes);
        // The row name must equal the index's name exactly — the build resolves by it.
        Assert.Equal("Base Garden", recipe.Name);
        Assert.Equal("Placeables", recipe.Category);  // grouped by the folder under Recipes/
        Assert.True(recipe.Editable);
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
