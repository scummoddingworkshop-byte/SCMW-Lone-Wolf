using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Knowledge;

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

    [AvaloniaFact]
    public void A_nested_plain_value_reads_as_a_breadcrumb_in_its_parents_group()
    {
        var catalog = new ContentCatalog(new NullSource());
        const string path = "_replenishableResourceDescription.PeriodicReplenishment.ReplenishAmount";

        var values = catalog.MapFields(new[]
        {
            new AssetField(path, path, StatusKind.ReadOnly, "0.1", "FloatProperty", Nested: true),
        }, editableItem: true);

        var value = Assert.Single(values);
        Assert.Equal("Replenish Amount", value.Name);   // just its own name, not the whole path
        Assert.Equal("Replenishable Resource Description › Periodic Replenishment", value.Group);
        Assert.Equal(path, value.TechnicalName);
        Assert.Equal(ValueBand.Normal, value.Band);
        Assert.True(value.Editable);   // a plain number: changeable like any other field
        Assert.False(value.ShowNote);
    }

    [AvaloniaFact]
    public void A_named_choice_is_changeable_with_a_one_line_note()
    {
        var catalog = new ContentCatalog(new NullSource());

        var values = catalog.MapFields(new[]
        {
            new AssetField("_baseElementType", "_baseElementType", StatusKind.ReadOnly,
                "EBaseElementType::Well", "NameProperty"),
        }, editableItem: true);

        var value = Assert.Single(values);
        Assert.Equal(ValueBand.Noted, value.Band);
        Assert.True(value.Editable);
        Assert.True(value.ShowNote);
        Assert.False(string.IsNullOrWhiteSpace(value.Note));
    }

    [AvaloniaFact]
    public void A_structural_value_is_locked()
    {
        var catalog = new ContentCatalog(new NullSource());

        var values = catalog.MapFields(new[]
        {
            new AssetField("_itemTags", "_itemTags", StatusKind.ReadOnly, "(tags)", "ArrayProperty"),
        }, editableItem: true);

        var value = Assert.Single(values);
        Assert.Equal(ValueBand.Locked, value.Band);
        Assert.False(value.Editable);
        Assert.True(value.IsLocked);
        Assert.Contains("not editable", value.LockedTooltip);
    }

    [AvaloniaFact]
    public void A_base_elements_dead_name_field_is_not_offered()
    {
        // _name builds and writes but the game's shown name comes from the element's type/class,
        // so the field is left out of the list entirely.
        var catalog = new ContentCatalog(new NullSource());

        var values = catalog.MapFields(new[]
        {
            new AssetField("_name", "_name", StatusKind.ReadOnly, "Well", "TextProperty"),
            new AssetField("_healthPoints", "_healthPoints", StatusKind.ReadOnly, "600", "IntProperty"),
        }, editableItem: true);

        Assert.DoesNotContain(values, v => v.TechnicalName == "_name");
        Assert.Contains(values, v => v.TechnicalName == "_healthPoints");
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

    [AvaloniaFact]
    public void An_ingredient_slots_amount_is_now_changeable()
    {
        var catalog = new ContentCatalog(new NullSource());
        const string path = "Ingredients[0].Amount.NoSkill";

        var values = catalog.MapFields(new[]
        {
            new AssetField(path, path, StatusKind.ReadOnly, "4", "IntProperty", Nested: true),
        }, editableItem: true);

        var value = Assert.Single(values);
        Assert.Equal("Amount › No Skill", value.Name);
        Assert.Equal("Ingredient 1", value.Group);
        Assert.Equal(ValueBand.Normal, value.Band);
        Assert.True(value.Editable);   // the build can now write into a list entry
    }

    [AvaloniaFact]
    public void An_ingredients_accepted_tag_is_changeable_with_a_link_note()
    {
        var catalog = new ContentCatalog(new NullSource());
        const string path = "Ingredients[0].AllowedTypes[0]";

        var values = catalog.MapFields(new[]
        {
            new AssetField(path, path, StatusKind.ReadOnly, "CI_Plank", "ObjectProperty", Nested: true),
        }, editableItem: true);

        var value = Assert.Single(values);
        Assert.Equal("Accepts 1", value.Name);
        Assert.Equal("Ingredient 1", value.Group);
        Assert.Equal(ValueBand.Noted, value.Band);
        Assert.True(value.Editable);
        Assert.True(value.ShowNote);
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
