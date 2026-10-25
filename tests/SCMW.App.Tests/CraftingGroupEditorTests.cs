using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The "Crafting group" card on a new recipe: which shelf it sits on and its number on that shelf.
/// The values are ordinary changes on the recipe's own path, so the build writes them unchanged.
/// </summary>
public class CraftingGroupEditorTests
{
    /// <summary>A fake game index with one recipe whose shelf is Melee Weapons, number 7.</summary>
    private sealed class RecipeSource : IContentSource
    {
        public const string Recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_1H_Stone_Axe";

        private static readonly AssetSummary Row =
            new(Recipe, "Stone Axe", "Items", StatusKind.Editable, 1, Recipe + ".uasset", null);

        /// <summary>Set false to model a recipe whose file has no shelf at all.</summary>
        public bool WithGroup { get; init; } = true;

        /// <summary>Set false to model a recipe with no order number (34 real ones have none).</summary>
        public bool WithOrder { get; init; } = true;

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Items" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= 1 ? Array.Empty<AssetSummary>() : new[] { Row });
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default)
        {
            var fields = new List<AssetField>
            {
                new("Product", "Product", StatusKind.ReadOnly,
                    "/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil_C"),
            };

            if (WithOrder)
                fields.Add(new AssetField("DisplayPriority", "DisplayPriority", StatusKind.ReadOnly, "7", "ByteProperty"));

            if (WithGroup)
            {
                fields.Add(new AssetField("CraftingCategoryTag.TagName", "CraftingCategoryTag.TagName",
                    StatusKind.ReadOnly, "CraftingCategory.Items.MeleeWeapons", "NameProperty", Nested: true));
            }

            return Task.FromResult<IReadOnlyList<AssetField>>(fields);
        }
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A small fixed menu, so these tests never pin one game build's numbers. Melee Weapons runs
    /// 0–6 and 8 — the donor's own number (7) is free here, which is how the "pick it back" undo
    /// path can be reached. Clothing Repairs is the game's real pair (Bone Needle 0, Sewing Kit 1),
    /// and a Base Building shelf proves the tab filter.
    /// </summary>
    private static RecipeMenuFile Menu() => new("test-build", "2026-10-23", new[]
    {
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_A", 0),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_B", 1),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_C", 2),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_D", 3),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_E", 4),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_F", 5),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_G", 6),
        new RecipePlace("CraftingCategory.Items.MeleeWeapons", "CR_H", 8),
        new RecipePlace("CraftingCategory.Items.Clothing.ClothingRepairs", "CR_Bone_Needle", 0),
        new RecipePlace("CraftingCategory.Items.Clothing.ClothingRepairs", "CR_Improvised_Sewing_Kit", 1),
        new RecipePlace("CraftingCategory.BaseBuilding.Miscellaneous", "CR_Base_Thing", 0),
    });

    private static (WorkspaceViewModel Vm, ItemChangeStore Changes, ProjectStore Projects, string Root)
        Ready(RecipeSource source)
    {
        var (projects, root) = TestProjects.With("Olive Press Mod");
        var changes = TestProjects.ChangeStore(projects);
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source),
                                        changes: changes, projects: projects, newAssets: new NewAssetStore(),
                                        menu: Menu());
        return (vm, changes, projects, root);
    }

    private static (WorkspaceViewModel Vm, ItemChangeStore Changes, ProjectStore Projects, string Root,
                    CraftingSectionStore Sections) ReadyWithSections(RecipeSource source)
    {
        var (projects, root) = TestProjects.With("Olive Press Mod");
        var changes = TestProjects.ChangeStore(projects);
        var sections = new CraftingSectionStore();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source),
                                        changes: changes, projects: projects, newAssets: new NewAssetStore(),
                                        menu: Menu(), sections: sections);
        return (vm, changes, projects, root, sections);
    }

    /// <summary>Make one new recipe from the fake donor, then open it.</summary>
    private static async Task<WorkspaceViewModel> NewRecipeOpen(WorkspaceViewModel vm)
    {
        await vm.OpenKindAsync("Recipes");
        vm.OpenNewRecipeCommand.Execute(null);
        vm.NewRecipeName = "Oil";
        vm.NewRecipeDonor = vm.RecipeDonors.Single(d => d.Name == "Stone Axe");
        vm.CreateNewRecipeCommand.Execute(null);

        await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "CR_Oil"));
        return vm;
    }

    [AvaloniaFact]
    public async Task A_new_recipe_shows_the_shelf_it_starts_on()
    {
        var (vm, _, _, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            Assert.True(vm.ShowCraftingGroup);
            Assert.Contains("Melee Weapons", vm.CraftingGroupNow);
            Assert.Contains("7", vm.CraftingGroupNow);
            Assert.Equal("Melee Weapons", vm.CraftingGroup?.Label);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_shelf_list_only_offers_the_same_menu_tab()
    {
        var (vm, _, _, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            Assert.NotEmpty(vm.CraftingGroupChoices);
            Assert.All(vm.CraftingGroupChoices, choice => Assert.Equal("Items", choice.Group.Tab));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Picking_a_shelf_records_the_tag_on_the_recipes_own_path()
    {        var (vm, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            var repairs = vm.CraftingGroupChoices.Single(c => c.Tag ==
                "CraftingCategory.Items.Clothing.ClothingRepairs");
            vm.CraftingGroup = repairs;

            var record = Assert.Single(changes.For(projects.Current!.FolderPath),
                                       r => r.FieldTechnicalName == "CraftingCategoryTag.TagName");
            Assert.Equal("CraftingCategory.Items.Clothing.ClothingRepairs", record.NewValue);
            Assert.Equal("CraftingCategory.Items.MeleeWeapons", record.OldValue);
            Assert.Equal(vm.SelectedAsset!.AssetId, record.AssetPath);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_new_section_is_saved_and_given_to_the_open_recipe()
    {
        var (vm, changes, projects, root, sections) = ReadyWithSections(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            vm.NewSectionName = "SCMW Mods";
            vm.CreateSectionCommand.Execute(null);

            var saved = Assert.Single(sections.Load(projects.Current!.FolderPath));
            Assert.Equal("SCMW Mods", saved.Name);
            Assert.Equal("CraftingCategory.Items.SCMW_Mods", saved.Tag);
            Assert.Equal(saved.Tag, vm.CraftingGroup?.Tag);

            var record = Assert.Single(changes.For(projects.Current!.FolderPath),
                                       r => r.FieldTechnicalName == "CraftingCategoryTag.TagName");
            Assert.Equal(saved.Tag, record.NewValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_section_whose_name_is_already_a_shelf_is_refused()
    {
        var (vm, _, projects, root, sections) = ReadyWithSections(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            vm.NewSectionName = "Melee Weapons";
            vm.CreateSectionCommand.Execute(null);

            Assert.Contains("already", vm.CraftingGroupNotice, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(sections.Load(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_a_section_this_recipe_uses_takes_it_away()
    {
        var (vm, changes, projects, root, sections) = ReadyWithSections(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);
            vm.NewSectionName = "SCMW Mods";
            vm.CreateSectionCommand.Execute(null);
            Assert.True(vm.CanRemoveSection);

            vm.RemoveSectionCommand.Execute(null);

            Assert.Empty(sections.Load(projects.Current!.FolderPath));
            Assert.DoesNotContain(changes.For(projects.Current.FolderPath),
                r => r.NewValue == "CraftingCategory.Items.SCMW_Mods");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_a_section_another_recipe_uses_is_refused()
    {
        var (vm, changes, projects, root, sections) = ReadyWithSections(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);
            vm.NewSectionName = "SCMW Mods";
            vm.CreateSectionCommand.Execute(null);
            changes.RecordEdit("OtherRecipe", "CraftingCategoryTag.TagName", "Old",
                "CraftingCategory.Items.SCMW_Mods", projects.Current!.Id, assetPath: "OtherRecipe");

            vm.RemoveSectionCommand.Execute(null);

            Assert.Contains("still uses", vm.CraftingGroupNotice, StringComparison.OrdinalIgnoreCase);
            Assert.Single(sections.Load(projects.Current.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_a_game_shelf_does_nothing()
    {
        var (vm, _, projects, root, sections) = ReadyWithSections(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            Assert.False(vm.CanRemoveSection);
            vm.RemoveSectionCommand.Execute(null);

            Assert.Empty(sections.Load(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Picking_a_place_records_the_order_number()
    {
        var (vm, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            vm.CraftingGroup = vm.CraftingGroupChoices.Single(c => c.Tag ==
                "CraftingCategory.Items.Clothing.ClothingRepairs");

            var atEnd = vm.CraftingPlaceChoices.Single(c => c.Label == "At the end of the list");
            Assert.Equal(2, atEnd.Number);   // the game's own two repairs are 0 and 1
            vm.CraftingPlace = atEnd;

            var record = Assert.Single(changes.For(projects.Current!.FolderPath),
                                       r => r.FieldTechnicalName == "DisplayPriority");
            Assert.Equal("2", record.NewValue);
            Assert.Equal("7", record.OldValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Picking_the_starting_number_back_drops_the_place_change()
    {
        var (vm, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            // The donor's own number is 7; on this fixed shelf 7 is free, so it can be picked back.
            var atEnd = vm.CraftingPlaceChoices.Single(c => c.Label == "At the end of the list");
            Assert.Equal(9, atEnd.Number);
            vm.CraftingPlace = atEnd;
            Assert.Contains(changes.For(projects.Current!.FolderPath),
                            r => r.FieldTechnicalName == "DisplayPriority" && r.NewValue == "9");

            var backToStart = vm.CraftingPlaceChoices.Single(c => c.Label == "After G");
            Assert.Equal(7, backToStart.Number);
            vm.CraftingPlace = backToStart;

            Assert.DoesNotContain(changes.For(projects.Current!.FolderPath),
                                  r => r.FieldTechnicalName == "DisplayPriority");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Picking_the_starting_shelf_back_drops_the_change()
    {
        var (vm, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            vm.CraftingGroup = vm.CraftingGroupChoices.Single(c => c.Tag ==
                "CraftingCategory.Items.Clothing.ClothingRepairs");
            vm.CraftingGroup = vm.CraftingGroupChoices.Single(c => c.Tag ==
                "CraftingCategory.Items.MeleeWeapons");

            Assert.DoesNotContain(changes.For(projects.Current!.FolderPath),
                                  r => r.FieldTechnicalName == "CraftingCategoryTag.TagName");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task An_ordinary_game_recipe_does_not_show_the_card()
    {
        var (vm, _, _, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Stone Axe"));

            Assert.False(vm.ShowCraftingGroup);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_group_values_leave_the_plain_field_list()
    {
        var (vm, _, _, root) = Ready(new RecipeSource());
        try
        {
            await NewRecipeOpen(vm);

            // The card owns both values now, so they are not shown twice (and the shelf struct's
            // "we haven't worked out what this does yet" row is gone too).
            Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "CraftingCategoryTag.TagName");
            Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "DisplayPriority");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_card_shows_a_working_shelf_dropdown_and_a_place_dropdown()
    {
        var (vm, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();

            await NewRecipeOpen(vm);
            window.UpdateLayout();

            var card = view.FindControl<Border>("CraftingGroupCard");
            Assert.NotNull(card);
            Assert.True(card!.IsVisible);

            var host = view.FindControl<ContentControl>("CraftingGroupHost");
            Assert.NotNull(host?.Content);
            var picker = host!.GetVisualDescendants().OfType<SearchPicker>()
                .Single(c => c.Name == "CraftingGroupPicker");
            picker.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(picker.FindControl<Popup>("Pop")!.IsOpen);

            // A shelf brings its own "Place" dropdown.
            vm.CraftingGroup = vm.CraftingGroupChoices.Single(c =>
                c.Tag == "CraftingCategory.Items.Clothing.ClothingRepairs");
            window.UpdateLayout();

            var placeHost = view.FindControl<ContentControl>("CraftingPlaceHost");
            Assert.NotNull(placeHost?.Content);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_recipe_without_an_order_number_keeps_the_shelf_but_hides_the_place()
    {
        var (vm, _, _, root) = Ready(new RecipeSource { WithOrder = false });
        try
        {
            await NewRecipeOpen(vm);

            Assert.True(vm.ShowCraftingGroup);
            Assert.True(vm.HasNoOrderField);
            Assert.False(vm.ShowCraftingPlace);
            Assert.Empty(vm.CraftingPlaceChoices);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_recipe_with_no_shelf_at_all_does_not_show_the_card()
    {
        var (vm, _, _, root) = Ready(new RecipeSource { WithGroup = false });
        try
        {
            await NewRecipeOpen(vm);

            Assert.False(vm.ShowCraftingGroup);
        }
        finally { TestProjects.Cleanup(root); }
    }
}
