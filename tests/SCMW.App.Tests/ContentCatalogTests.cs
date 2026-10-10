using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

public class ContentCatalogTests
{
    private static AssetField Field(string technical, string value, StatusKind status = StatusKind.ReadOnly) =>
        new(FriendlyName: technical, TechnicalName: technical, Status: status, CurrentValue: value);

    [AvaloniaFact]
    public void An_asset_becomes_an_item_row()
    {
        var catalog = new ContentCatalog(new NullSource());
        var items = catalog.MapItems(new[]
        {
            new AssetSummary("SCUM/.../Items/Weapons/Ranged_Weapons/Improvised_Bow", "Improvised Bow",
                "Weapons", StatusKind.Unknown, 0, "SCUM/.../Improvised_Bow.uasset", null),
        });

        var item = Assert.Single(items);
        Assert.Equal("Improvised Bow", item.Name);
        Assert.Equal("Weapons", item.Category);
    }

    [AvaloniaFact]
    public void A_technical_field_becomes_a_friendly_labelled_value_in_a_group()
    {
        var catalog = new ContentCatalog(new NullSource());
        var values = catalog.MapFields(new[]
        {
            Field("_drawWeight", "2.5"),
            Field("MaxStackSize", "25"),
        });

        var draw = values.First(v => v.Value == "2.5");
        Assert.Equal("Draw Weight", draw.Name);       // auto-tidied by FieldNames
        Assert.Equal("_drawWeight", draw.TechnicalName); // the game's real name, kept for building
        Assert.False(draw.Editable);

        var stack = values.First(v => v.Value == "25");
        Assert.Equal("Max Stack", stack.Name);        // exact dictionary label
        Assert.Equal("Inventory", stack.Group);
    }

    [AvaloniaFact]
    public void Only_a_field_the_engine_rates_editable_shows_as_changeable()
    {
        var catalog = new ContentCatalog(new NullSource());
        var values = catalog.MapFields(new[] { Field("Damage", "45", StatusKind.Editable) });

        Assert.True(Assert.Single(values).Editable);
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
