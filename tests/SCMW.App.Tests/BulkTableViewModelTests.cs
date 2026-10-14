using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The bulk table (Task 5.1): pick many files — or "all Food" — and set one value once, so each
/// chosen file gets its own recorded change. Changes are always addressed by the file's PATH.
/// </summary>
public class BulkTableViewModelTests
{
    // The plan's test: one change produced per selected asset.
    [AvaloniaFact]
    public async Task Applying_a_value_records_one_change_per_selected_asset()
    {
        var (projects, root) = TestProjects.With("Food mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new BulkSource();
            var vm = new BulkTableViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes, projects);

            await vm.OpenKindAsync("Items");

            vm.Rows.Single(r => r.Name == "Tomato").IsSelected = true;
            vm.Rows.Single(r => r.Name == "Canned Beans").IsSelected = true;
            vm.SelectedField = vm.Fields.Single(f => f.Name == "MaxQuantity");
            vm.Value = "40";

            await vm.ApplyAsync();

            var records = changes.For(projects.Current!.FolderPath);
            Assert.Equal(2, records.Count);   // one per selected asset, and only the selected ones
            Assert.All(records, r => Assert.Equal("MaxQuantity", r.FieldTechnicalName));
            Assert.All(records, r => Assert.Equal("40", r.NewValue));
            Assert.All(records, r => Assert.Equal(ChangeOperation.Set, r.Operation));
            Assert.Contains(records, r => r.AssetPath == "/items/Food/Tomato_ES");
            Assert.Contains(records, r => r.AssetPath == "/items/Food/Canned_Beans_ES");
            Assert.DoesNotContain(records, r => r.AssetPath == "/items/Weapons/Fire_Axe_ES");
            Assert.All(records, r => Assert.Equal("10", r.OldValue));   // the file's own value, as "was"
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // "select all" ticks every file the filters show
    public async Task Select_all_then_apply_writes_one_change_for_every_shown_file()
    {
        var (projects, root) = TestProjects.With("Food mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new BulkSource();
            var vm = new BulkTableViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes, projects);

            await vm.OpenKindAsync("Items");
            vm.SelectAllCommand.Execute(null);
            Assert.Equal(vm.Rows.Count, vm.SelectedCount);

            vm.SelectedField = vm.Fields.Single(f => f.Name == "MaxQuantity");
            vm.Value = "40";
            await vm.ApplyAsync();

            Assert.Equal(3, changes.For(projects.Current!.FolderPath).Count);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // "all Food": the category chip narrows what "select all" covers
    public async Task Select_all_covers_only_the_chosen_category()
    {
        var (projects, root) = TestProjects.With("Food mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new BulkSource();
            var vm = new BulkTableViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes, projects);

            await vm.OpenKindAsync("Items");
            vm.SelectCategoryCommand.Execute("Food");
            vm.SelectAllCommand.Execute(null);

            vm.SelectedField = vm.Fields.Single(f => f.Name == "MaxQuantity");
            vm.Value = "40";
            await vm.ApplyAsync();

            var records = changes.For(projects.Current!.FolderPath);
            Assert.Equal(2, records.Count);   // the two Food files, not the weapon
            Assert.All(records, r => Assert.StartsWith("/items/Food/", r.AssetPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // "one project = one mod": with nothing chosen, the bulk set writes nothing
    public async Task Applying_with_no_chosen_project_records_nothing_and_says_so()
    {
        var (projects, root) = TestProjects.With("Food mod");
        try
        {
            projects.SetCurrent("");   // a project exists, but the player has not picked one
            var changes = TestProjects.ChangeStore(projects);
            var source = new BulkSource();
            var vm = new BulkTableViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes, projects);

            await vm.OpenKindAsync("Items");
            vm.SelectAllCommand.Execute(null);
            vm.SelectedField = vm.Fields.Single(f => f.Name == "MaxQuantity");
            vm.Value = "40";

            Assert.True(vm.NeedsProject);

            await vm.ApplyAsync();

            Assert.Empty(changes.For(projects.Current!.FolderPath));    // nothing recorded
            Assert.False(string.IsNullOrWhiteSpace(vm.ResultText));     // and it says why
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // a value that does not fit the field's kind is refused, not written
    public async Task A_value_that_is_not_a_number_is_refused()
    {
        var (projects, root) = TestProjects.With("Food mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new BulkSource();
            var vm = new BulkTableViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes, projects);

            await vm.OpenKindAsync("Items");
            vm.SelectAllCommand.Execute(null);
            vm.SelectedField = vm.Fields.Single(f => f.Name == "MaxQuantity");   // a number
            vm.Value = "lots";

            Assert.False(vm.IsValueValid);
            Assert.False(vm.CanApply);

            await vm.ApplyAsync();   // a refused apply is a no-op, never a bad write
            Assert.Empty(changes.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    /// <summary>A fake game index: three items (two Food, one Weapon), each with a MaxQuantity field.</summary>
    private sealed class BulkSource : IContentSource
    {
        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary("/items/Food/Tomato_ES", "Tomato", "Food",
                StatusKind.Editable, 1, "/items/Food/Tomato_ES.uasset", null),
            new AssetSummary("/items/Food/Canned_Beans_ES", "Canned Beans", "Food",
                StatusKind.Editable, 1, "/items/Food/Canned_Beans_ES.uasset", null),
            new AssetSummary("/items/Weapons/Fire_Axe_ES", "Fire Axe", "Weapons",
                StatusKind.Editable, 1, "/items/Weapons/Fire_Axe_ES.uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Food", "Weapons" };
        public void Configure(string p, string? k, string? b) { }

        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100,
                                                             CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(
                s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());

        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Rows.Any(r => r.Id == id)
                ? new[] { new AssetField("MaxQuantity", "MaxQuantity", StatusKind.Editable, "10") }
                : Array.Empty<AssetField>());

        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null,
                                                                 CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));

        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids,
            IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));

        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
