using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
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

    [AvaloniaFact]
    public async Task Committing_an_edit_records_a_change_against_the_files_path()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new BrowseSource();
            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes: changes, projects: projects);

            await vm.OpenKindAsync("Items");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Batteries"));

            var weight = vm.Fields.Single(f => f.TechnicalName == "Weight");
            vm.BeginEditCommand.Execute(weight);
            weight.Draft = "999";
            vm.CommitEditCommand.Execute(weight);

            var record = Assert.Single(changes.For(projects.Current!.FolderPath));
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES", record.AssetPath);
            Assert.Equal("999", record.NewValue);
            Assert.Equal(ChangeOperation.Set, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Multiplying_a_number_field_records_a_multiply_against_the_files_path()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new BrowseSource();
            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes: changes, projects: projects);

            await vm.OpenKindAsync("Items");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Batteries"));

            var weight = vm.Fields.Single(f => f.TechnicalName == "Weight");
            vm.BeginMultiplyCommand.Execute(weight);
            weight.FactorDraft = "2";
            vm.CommitMultiplyCommand.Execute(weight);

            var record = Assert.Single(changes.For(projects.Current!.FolderPath));
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES", record.AssetPath);
            Assert.Equal("2", record.NewValue);
            Assert.Equal(ChangeOperation.Multiply, record.Operation);
            Assert.Equal("800", weight.Value);   // the game's 400, doubled
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // "one project = one mod": editing with nothing chosen is refused, not silently retargeted
    public async Task An_edit_with_no_chosen_project_is_refused_and_explained()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            projects.SetCurrent("");   // projects exist, but the player has not picked one
            var changes = TestProjects.ChangeStore(projects);
            var source = new BrowseSource();
            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                            new ContentCatalog(source), changes: changes, projects: projects);

            await vm.OpenKindAsync("Items");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Batteries"));

            Assert.True(vm.NeedsProject);

            var weight = vm.Fields.Single(f => f.TechnicalName == "Weight");
            vm.BeginEditCommand.Execute(weight);
            weight.Draft = "999";
            vm.CommitEditCommand.Execute(weight);

            Assert.Empty(changes.For(projects.Current!.FolderPath));   // nothing recorded
            Assert.False(string.IsNullOrWhiteSpace(vm.ProjectWarning)); // and it says why

            // Pick the project: the same edit now records.
            projects.SetCurrent(projects.Current!.Id);
            Assert.False(vm.NeedsProject);

            vm.BeginEditCommand.Execute(weight);
            weight.Draft = "77";
            vm.CommitEditCommand.Execute(weight);
            Assert.Single(changes.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // Review Focus 4: two files with one name show their own change, by path
    public void Two_files_that_share_a_display_name_show_their_own_value()
    {
        var (projects, root) = TestProjects.With("Dup mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordEdit("Duct Tape", "Weight", "10", "20", projects.Current!.Id,
                               assetPath: "/a/Duct_Tape");

            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), changes: changes, projects: projects);
            var fields = new[] { Field("Weight", "10") };

            vm.Show("Items", "Duct Tape", fields, "/a/Duct_Tape");
            Assert.Equal("20", vm.Fields.Single().Value);   // the edit landed on file A

            vm.Show("Items", "Duct Tape", fields, "/b/Duct_Tape");
            Assert.Equal("10", vm.Fields.Single().Value);   // file B, same name, is untouched
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // Review Focus 5: a string-table key is shown, but never as editable words
    public void A_string_table_key_field_is_shown_without_an_editor()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Batteries", new[]
        {
            new AssetField("Caption", "Caption", StatusKind.ReadOnly, "Recipe_AnyBluntTool"),
        });

        var caption = vm.Fields.Single();
        Assert.Equal("Recipe_AnyBluntTool", caption.Value);   // the key is shown honestly
        Assert.False(caption.Writable);
        Assert.False(caption.ShowValueButton);                // and there is no editor
    }

    // --- Fields you could add (Task 5.2) --------------------------------------------------------

    [AvaloniaFact]
    public async Task The_add_list_offers_only_the_flagged_addable_field()
    {
        var source = new AddFieldSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source));

        await vm.OpenKindAsync("Items");
        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Tomato"));

        // The auto-destruct timer is the only addable field, and a donor (Stone) has it.
        var add = Assert.Single(vm.AddableFields);
        Assert.Equal("_autoDestructTime", add.TechnicalName);

        // Not offered: a field already in the file, a field with no donor anywhere, and a field that
        // has a donor but the library does not flag addable.
        Assert.DoesNotContain(vm.AddableFields, a => a.TechnicalName == "Weight");
        Assert.DoesNotContain(vm.AddableFields, a => a.TechnicalName == "ShelfLife");
        Assert.DoesNotContain(vm.AddableFields, a => a.TechnicalName == "MaxWaterWeightRatio");
    }

    [AvaloniaFact]
    public async Task Adding_a_field_writes_it_to_the_items_class_file_with_the_donor()
    {
        var (projects, root) = TestProjects.With("Tomato mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var source = new AddFieldSource();
            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source),
                                            changes: changes, projects: projects);

            await vm.OpenKindAsync("Items");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Tomato"));

            var add = vm.AddableFields.Single(a => a.TechnicalName == "_autoDestructTime");
            add.Draft = "0";
            vm.AddCommand.Execute(add);

            var record = Assert.Single(changes.For(projects.Current!.FolderPath));
            Assert.Equal(ChangeOperation.AddField, record.Operation);
            Assert.Equal("_autoDestructTime", record.FieldTechnicalName);
            Assert.Equal("0", record.NewValue);
            Assert.Equal(AddFieldSource.TomatoClass, record.AssetPath);   // the class file, not the item file
            Assert.Equal(AddFieldSource.TorchClass, record.DonorPath);    // where the shape is borrowed from
            Assert.DoesNotContain(vm.AddableFields, a => a.TechnicalName == "_autoDestructTime");

            // The item row now shows that a field was added.
            var row = vm.VisibleAssets.Single(a => a.Name == "Tomato");
            Assert.True(row.HasAddedFieldNote);
            Assert.Contains("added", row.AddedFieldNote);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_field_already_added_is_not_offered_again()
    {
        var (projects, root) = TestProjects.With("Tomato mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            changes.RecordAddField(AddFieldSource.TomatoClass, "_autoDestructTime", "0", AddFieldSource.TorchClass,
                                   projects.Current!.Id, assetPath: AddFieldSource.TomatoClass);

            var source = new AddFieldSource();
            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source),
                                            changes: changes, projects: projects);

            await vm.OpenKindAsync("Items");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Tomato"));

            Assert.DoesNotContain(vm.AddableFields, a => a.TechnicalName == "_autoDestructTime");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_add_section_shows_a_field_you_could_add()
    {
        var vm = new WorkspaceViewModel();
        var addable = new AddableField("Auto-destruct time", "real minutes",
            "How long an item left on the ground lasts before it vanishes.",
            "_autoDestructTime", "/a/Torch", FieldType.Number, "/a/Tomato");

        vm.Show("Items", "Tomato", new[] { Field("Weight", "20000") }, "/a/Tomato_ES",
                new[] { addable });

        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        window.UpdateLayout();

        var section = view.FindControl<Border>("AddableSection");
        Assert.NotNull(section);
        Assert.True(section!.IsVisible);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("Fields you could add", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Auto-destruct time", texts);
    }

    [AvaloniaFact]
    public async Task Each_item_row_carries_its_icon()
    {
        var source = new IconSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source, source.Icons));

        await vm.OpenKindAsync("Items");

        var row = Assert.Single(vm.VisibleAssets);
        Assert.Equal(IconSource.IconPath, row.IconPath);
        Assert.Contains(IconSource.Batteries, source.ExtractedIcons);
    }

    [AvaloniaFact]
    public async Task Base_parts_and_recipes_also_carry_their_icon()
    {
        var source = new IconSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source, source.Icons));

        await vm.OpenKindAsync("Base building");
        Assert.Equal(IconSource.IconPath, Assert.Single(vm.VisibleAssets).IconPath);

        await vm.OpenKindAsync("Recipes");
        Assert.Equal(IconSource.IconPath, Assert.Single(vm.VisibleAssets).IconPath);

        Assert.Contains(IconSource.BaseWell, source.ExtractedIcons);
        Assert.Contains(IconSource.Recipe, source.ExtractedIcons);
    }

    [AvaloniaFact]
    public async Task The_donor_search_looks_past_the_first_forty_items()
    {
        var source = new DeepDonorSource();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source));

        await vm.OpenKindAsync("Items");
        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Target"));

        // The only donor sits at item 46, past the old 40-item cap.
        var add = Assert.Single(vm.AddableFields);
        Assert.Equal("_autoDestructTime", add.TechnicalName);
        Assert.Equal(DeepDonorSource.DonorClass, add.DonorPath);
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

    /// <summary>A fake index for the add-a-field test: Tomato lacks the timer; a torch's CLASS file
    /// (where the game keeps the timer) is the donor. The timer must land on Tomato's class file.</summary>
    private sealed class AddFieldSource : IContentSource
    {
        public const string Tomato = "SCUM/Content/ConZ_Files/Items/Food/Tomato_ES";
        public const string TomatoClass = "SCUM/Content/ConZ_Files/Items/Food/Tomato";
        public const string Torch = "SCUM/Content/ConZ_Files/Items/Food/Torch_ES";
        public const string TorchClass = "SCUM/Content/ConZ_Files/Items/Food/Torch";

        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(Tomato, "Tomato", "Food", StatusKind.Editable, 1, Tomato + ".uasset", null),
            new AssetSummary(Torch, "Torch", "Food", StatusKind.Editable, 1, Torch + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Food" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(id switch
            {
                Tomato => new[] { Field("Weight", "20000") },                     // item file: no timer
                TorchClass => new[] { Field("Weight", "5000"), Field("_autoDestructTime", "5"),
                                      Field("MaxWaterWeightRatio", "1") },        // the donor's class file
                _ => new[] { Field("Weight", "1000") },                           // Tomato class, Torch item, others
            });
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A fake source with one item and an icon cache, so the icon path can be checked.</summary>
    private sealed class IconSource : IContentSource
    {
        public const string Batteries = "SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES";
        public const string BaseWell = "SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/BP_Base_Well";
        public const string Recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Wooden_Arrow";
        public const string IconPath = @"C:\icons\batteries.png";

        public readonly List<string> ExtractedIcons = new();

        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(Batteries, "Batteries ES", "Equipment", StatusKind.Editable, 1,
                Batteries + ".uasset", null),
            new AssetSummary(BaseWell, "Well (base)", "Base elements", StatusKind.Editable, 1,
                BaseWell + ".uasset", null),
            new AssetSummary(Recipe, "Wooden Arrow", "Items", StatusKind.Editable, 1,
                Recipe + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons { get; } = new FakeIconCache();
        public IReadOnlyList<string> Categories => new[] { "Equipment" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default)
        {
            ExtractedIcons.AddRange(ids);
            return Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        }
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;

        private sealed class FakeIconCache : IIconCache
        {
            public string IconsDirectory => "";
            public string? GetIconPath(string assetId) => IconPath;
            public int Count => 1;
            public long TotalBytes => 0;
            public void Clear() { }
        }
    }

    /// <summary>A fake index whose only timer donor sits at item 46 — past the old 40-item cap.</summary>
    private sealed class DeepDonorSource : IContentSource
    {
        public const string Target = "SCUM/Content/ConZ_Files/Items/A/Target_ES";
        public const string DonorItem = "SCUM/Content/ConZ_Files/Items/A/DeepDonor_ES";
        public const string DonorClass = "SCUM/Content/ConZ_Files/Items/A/DeepDonor";

        private static readonly IReadOnlyList<AssetSummary> Rows = Build();

        private static IReadOnlyList<AssetSummary> Build()
        {
            var rows = new List<AssetSummary> { Row(Target) };
            for (var i = 0; i < 44; i++) rows.Add(Row($"SCUM/Content/ConZ_Files/Items/A/Filler{i:D2}_ES"));
            rows.Add(Row(DonorItem));
            for (var i = 44; i < 49; i++) rows.Add(Row($"SCUM/Content/ConZ_Files/Items/A/Filler{i:D2}_ES"));
            return rows;
        }

        private static AssetSummary Row(string id) =>
            new(id, id, "Equipment", StatusKind.Editable, 1, id + ".uasset", null);

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Equipment" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(id == DonorClass
                ? new[] { Field("Weight", "5000"), Field("_autoDestructTime", "5") }   // the donor's class file
                : new[] { Field("Weight", "1000") });
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
