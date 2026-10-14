using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

public class ItemsRealSourceTests
{
    /// <summary>A fake content source: no game, no disk.</summary>
    private sealed class FakeSource : IContentSource
    {
        private readonly IReadOnlyList<AssetSummary> _items;
        private readonly Dictionary<string, IReadOnlyList<AssetField>> _fields;

        public FakeSource(IReadOnlyList<AssetSummary> items,
                          Dictionary<string, IReadOnlyList<AssetField>> fields,
                          ContentSourceStatus status)
        {
            _items = items; _fields = fields; Status = status;
        }

        public ContentSourceStatus Status { get; }
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => _items.Select(i => i.Category).Distinct().ToList();
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult(_items);
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_fields.TryGetValue(id, out var f) ? f : Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static FakeSource Ready() => new(
        new[]
        {
            new AssetSummary("SCUM/.../Improvised_Bow_ES", "Improvised Bow", "Weapons",
                StatusKind.Unknown, 0, "SCUM/.../Improvised_Bow_ES.uasset", null),
        },
        new Dictionary<string, IReadOnlyList<AssetField>>
        {
            ["SCUM/.../Improvised_Bow_ES"] = new[]
            {
                new AssetField("_drawWeight", "_drawWeight", StatusKind.ReadOnly, "2.5", "FloatProperty"),
            },
        },
        new ContentSourceStatus(ContentSourceState.Ready, "mounted", "25247492"));

    [AvaloniaFact]
    public async Task A_ready_but_unscanned_source_is_scanned_before_listing()
    {
        // The real source reports Ready the moment it is configured, but its index is only built
        // by ScanAsync. If we skip the scan, a fresh install silently shows samples.
        var source = new ScanThenReadySource();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));

        await vm.LoadFromSourceAsync();

        Assert.True(source.Scanned, "the source was never scanned");
        Assert.True(vm.IsUsingRealItems);
        Assert.Contains(vm.VisibleItems, i => i.Name == "Improvised Bow");
    }

    /// <summary>Ready, but returns nothing until scanned — like the real index-backed source.</summary>
    private sealed class ScanThenReadySource : IContentSource
    {
        private bool _scanned;

        public bool Scanned => _scanned;
        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Weapons" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default)
        {
            _scanned = true;
            return Task.FromResult(Status);
        }

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(_scanned
                ? new[]
                {
                    new AssetSummary("SCUM/.../Improvised_Bow_ES", "Improvised Bow", "Weapons",
                        StatusKind.Unknown, 0, "SCUM/.../Improvised_Bow_ES.uasset", null),
                }
                : Array.Empty<AssetSummary>());

        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static FakeSource NotConfigured() => new(
        Array.Empty<AssetSummary>(),
        new Dictionary<string, IReadOnlyList<AssetField>>(),
        new ContentSourceStatus(ContentSourceState.NotConfigured, "no path", null));

    [AvaloniaFact]
    public async Task A_ready_source_replaces_the_sample_list_with_real_items()
    {
        var source = Ready();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));

        await vm.LoadFromSourceAsync();

        Assert.True(vm.IsUsingRealItems);
        Assert.Contains(vm.VisibleItems, i => i.Name == "Improvised Bow");
        Assert.DoesNotContain(vm.VisibleItems, i => i.Name == "Duct Tape");
    }

    [AvaloniaFact]
    public async Task A_ready_source_rebuilds_the_category_chips()
    {
        var source = Ready();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));

        await vm.LoadFromSourceAsync();

        Assert.Contains("Weapons", vm.Categories);
        Assert.DoesNotContain("Tools", vm.Categories);   // a sample-only category
    }

    [AvaloniaFact]
    public async Task A_not_configured_source_keeps_the_samples_and_says_so()
    {
        var source = NotConfigured();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));

        await vm.LoadFromSourceAsync();

        Assert.False(vm.IsUsingRealItems);
        Assert.Contains(vm.VisibleItems, i => i.Name == "Duct Tape");
        Assert.Contains("sample", vm.SourceNote, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void With_no_source_the_list_is_samples_and_no_note()
    {
        var vm = new ItemsViewModel();

        Assert.False(vm.IsUsingRealItems);
        Assert.Equal("", vm.SourceNote);
        Assert.Contains(vm.VisibleItems, i => i.Name == "Duct Tape");
    }

    [AvaloniaFact]
    public async Task Select_a_real_item_and_its_fields_load_with_friendly_labels()
    {
        var source = Ready();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));
        await vm.LoadFromSourceAsync();

        vm.ShowAllValues = true;
        vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Improvised Bow");
        await vm.LoadFieldsAsync(vm.SelectedItem);

        var values = vm.Sections.SelectMany(s => s.Values).ToList();
        Assert.Contains(values, v => v.Name == "Draw Weight" && v.Value == "2.5");
    }

    [AvaloniaFact]
    public async Task Editing_a_real_item_records_the_games_field_name_not_the_label()
    {
        // The build needs the game's own property name (e.g. "_drawWeight"); the screen shows
        // a friendly label ("Draw Weight"). Recording the label made the build fail to find it.
        var source = Ready();
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = TestProjects.ChangeStore(projects);
            var vm = new ItemsViewModel(store, projects, source: source, catalog: new ContentCatalog(source));
            await vm.LoadFromSourceAsync();
            vm.ShowAllValues = true;
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Improvised Bow");
            await vm.LoadFieldsAsync(vm.SelectedItem);

            var draw = vm.Sections.SelectMany(s => s.Values).First(v => v.Name == "Draw Weight");
            vm.BeginEditCommand.Execute(draw);
            draw.Draft = "3.5";
            vm.CommitEditCommand.Execute(draw);

            var record = Assert.Single(store.For(projects.Current!.FolderPath));
            Assert.Equal("_drawWeight", record.FieldTechnicalName);
            // ...and the file's own path is kept too, so the build never has to guess by name.
            Assert.Equal("SCUM/.../Improvised_Bow_ES", record.AssetPath);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // Review Focus 4: two files with one display name keep their own edits (by path)
    public async Task Two_items_that_share_a_display_name_keep_their_own_edited_values()
    {
        const string pathA = "SCUM/Content/A/Duct_Tape_ES";
        const string pathB = "SCUM/Content/B/Duct_Tape_ES";

        var source = new FakeSource(
            new[]
            {
                new AssetSummary(pathA, "Duct Tape ES", "Equipment", StatusKind.Editable, 1, pathA + ".uasset", null),
                new AssetSummary(pathB, "Duct Tape ES", "Equipment", StatusKind.Editable, 1, pathB + ".uasset", null),
            },
            new Dictionary<string, IReadOnlyList<AssetField>>
            {
                [pathA] = new[] { new AssetField("_weight", "_weight", StatusKind.Editable, "10") },
                [pathB] = new[] { new AssetField("_weight", "_weight", StatusKind.Editable, "10") },
            },
            new ContentSourceStatus(ContentSourceState.Ready, "mounted", "1"));

        var (projects, root) = TestProjects.With("Dup mod");
        try
        {
            var store = TestProjects.ChangeStore(projects);

            // Edit only the FIRST file; the two share the display name "Duct Tape ES".
            store.RecordEdit("Duct Tape ES", "_weight", "10", "20", projects.Current!.Id, assetPath: pathA);

            var vm = new ItemsViewModel(store, projects, source: source, catalog: new ContentCatalog(source));
            await vm.LoadFromSourceAsync();

            var a = vm.VisibleItems.Single(i => i.AssetId == pathA);
            var b = vm.VisibleItems.Single(i => i.AssetId == pathB);
            await vm.LoadFieldsAsync(a);
            await vm.LoadFieldsAsync(b);

            Assert.Equal("20", a.Values.Single(v => v.TechnicalName == "_weight").Value);   // A got the edit
            Assert.Equal("10", b.Values.Single(v => v.TechnicalName == "_weight").Value);   // B is untouched
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_nested_value_is_shown_and_recorded_by_its_dotted_path()
    {
        const string path = "_replenishableResourceDescription.PeriodicReplenishment.ReplenishAmount";
        var source = new FakeSource(
            new[]
            {
                new AssetSummary("SCUM/.../Well_ES", "Well", "Base elements",
                    StatusKind.Unknown, 0, "SCUM/.../Well_ES.uasset", null),
            },
            new Dictionary<string, IReadOnlyList<AssetField>>
            {
                ["SCUM/.../Well_ES"] = new[]
                {
                    new AssetField(path, path, StatusKind.ReadOnly, "0.1", "FloatProperty", Nested: true),
                },
            },
            new ContentSourceStatus(ContentSourceState.Ready, "mounted", "1"));

        var (projects, root) = TestProjects.With("Nested mod");
        try
        {
            var store = TestProjects.ChangeStore(projects);
            var vm = new ItemsViewModel(store, projects, source: source, catalog: new ContentCatalog(source));
            await vm.LoadFromSourceAsync();
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Well");
            await vm.LoadFieldsAsync(vm.SelectedItem);

            var value = Assert.Single(vm.Sections.SelectMany(s => s.Values));
            Assert.True(value.Editable);
            Assert.Equal("Replenish Amount", value.Name);   // its own name, not the whole path

            vm.BeginEditCommand.Execute(value);
            value.Draft = "0.3";
            vm.CommitEditCommand.Execute(value);

            var record = Assert.Single(store.For(projects.Current!.FolderPath));
            Assert.Equal(path, record.FieldTechnicalName);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Adding_an_ingredient_records_a_list_change_and_shows_a_new_slot()
    {
        var source = new FakeSource(
            new[]
            {
                new AssetSummary("SCUM/.../CR_Thing", "Thing", "Items",
                    StatusKind.Unknown, 0, "SCUM/.../CR_Thing.uasset", null),
            },
            new Dictionary<string, IReadOnlyList<AssetField>>
            {
                ["SCUM/.../CR_Thing"] = new[]
                {
                    new AssetField("Ingredients[0].Amount.NoSkill",
                        "Ingredients[0].Amount.NoSkill", StatusKind.ReadOnly, "4", "IntProperty", Nested: true),
                },
            },
            new ContentSourceStatus(ContentSourceState.Ready, "mounted", "1"));

        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var store = TestProjects.ChangeStore(projects);
            var vm = new ItemsViewModel(store, projects, source: source,
                catalog: new ContentCatalog(source), family: AssetFamily.Recipes);
            await vm.LoadFromSourceAsync();
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Thing");
            await vm.LoadFieldsAsync(vm.SelectedItem);

            Assert.True(vm.CanManageIngredients);
            Assert.Single(vm.Sections);

            vm.AddIngredientCommand.Execute(null);

            var record = Assert.Single(store.For(projects.Current!.FolderPath));
            Assert.Equal(ChangeOperation.AddEntry, record.Operation);
            Assert.Equal("Ingredients", record.FieldTechnicalName);
            Assert.Equal(2, vm.Sections.Count);   // a second slot now shows
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_an_ingredient_clears_the_recipes_value_edits()
    {
        var source = new FakeSource(
            new[]
            {
                new AssetSummary("SCUM/.../CR_Thing", "Thing", "Items",
                    StatusKind.Unknown, 0, "SCUM/.../CR_Thing.uasset", null),
            },
            new Dictionary<string, IReadOnlyList<AssetField>>
            {
                ["SCUM/.../CR_Thing"] = new[]
                {
                    new AssetField("Ingredients[0].Amount.NoSkill",
                        "Ingredients[0].Amount.NoSkill", StatusKind.ReadOnly, "4", "IntProperty", Nested: true),
                    new AssetField("Ingredients[1].Amount.NoSkill",
                        "Ingredients[1].Amount.NoSkill", StatusKind.ReadOnly, "5", "IntProperty", Nested: true),
                },
            },
            new ContentSourceStatus(ContentSourceState.Ready, "mounted", "1"));

        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var store = TestProjects.ChangeStore(projects);
            var vm = new ItemsViewModel(store, projects, source: source,
                catalog: new ContentCatalog(source), family: AssetFamily.Recipes);
            await vm.LoadFromSourceAsync();
            vm.SelectedItem = vm.VisibleItems.First(i => i.Name == "Thing");
            await vm.LoadFieldsAsync(vm.SelectedItem);

            // Edit slot 0's amount...
            var value = vm.Sections.SelectMany(s => s.Values)
                .First(v => v.TechnicalName == "Ingredients[0].Amount.NoSkill");
            vm.BeginEditCommand.Execute(value);
            value.Draft = "9";
            vm.CommitEditCommand.Execute(value);
            Assert.Contains(store.For(projects.Current!.FolderPath), r => r.Operation == ChangeOperation.Set);

            // ...then remove a slot. The value edit is cleared, so it cannot land on the wrong
            // ingredient after the slots shift.
            vm.RemoveIngredientCommand.Execute("Ingredients[0]");

            var records = store.For(projects.Current!.FolderPath);
            Assert.DoesNotContain(records, r => r.Operation == ChangeOperation.Set);
            Assert.Contains(records, r => r.Operation == ChangeOperation.RemoveEntry
                                          && r.FieldTechnicalName == "Ingredients[0]");
            // ...and the screen says plainly that the edits were reset.
            Assert.Contains("reset", vm.RefusalMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Every_item_is_loaded_not_just_the_first_page()
    {
        const int total = 1200;
        var source = new PagedSource(total);
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));

        await vm.LoadFromSourceAsync();

        Assert.Equal(total, vm.VisibleItems.Count);
    }

    [AvaloniaFact]
    public async Task A_sample_only_category_resets_when_real_items_load()
    {
        var source = Ready();
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));
        vm.SelectedCategory = "Tools";                 // a category that only the samples had

        await vm.LoadFromSourceAsync();

        Assert.Equal("All", vm.SelectedCategory);      // reset instead of "No items match that"
        Assert.NotEmpty(vm.VisibleItems);
    }

    [AvaloniaFact]
    public void An_item_with_no_values_says_so_plainly()
    {
        var item = new ItemSummary("Mystery", "Equipment", "desc", editable: false);
        var vm = new ItemsViewModel(items: new[] { item });

        vm.SelectedItem = item;

        Assert.True(vm.ShowNoValues);
        Assert.Contains("No values", vm.NoValuesText);
    }

    /// <summary>A ready source that honours paging, so the loader must page to get everything.</summary>
    private sealed class PagedSource : IContentSource
    {
        private readonly IReadOnlyList<AssetSummary> _items;

        public PagedSource(int count) => _items = Enumerable.Range(0, count)
            .Select(i => new AssetSummary($"SCUM/.../Items/Item_{i:D5}_ES", $"Item {i:D5}", "Equipment",
                StatusKind.Unknown, 0, $"SCUM/.../Item_{i:D5}_ES.uasset", null))
            .ToList();

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Equipment" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) => Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(_items.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}

