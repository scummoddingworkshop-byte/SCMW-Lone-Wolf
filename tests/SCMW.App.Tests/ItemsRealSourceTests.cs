using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

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
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Every_item_is_loaded_not_just_the_first_page()
    {
        const int total = 1200;
        var source = new PagedSource(total);
        var vm = new ItemsViewModel(source: source, catalog: new ContentCatalog(source));
        vm.OnlyEditable = false;   // no _ES filter, so the visible count is the whole set

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

