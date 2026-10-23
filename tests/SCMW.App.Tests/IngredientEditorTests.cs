using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The recipe page's Ingredients card: one row per ingredient slot, each showing the accepted tag
/// and the five per-skill amounts, editable like any other value. The card owns those values, so
/// they leave the plain (locked) field list.
/// </summary>
public class IngredientEditorTests
{
    private static AssetField Field(string technical, string value, string hint = "IntProperty") =>
        new(technical, technical, StatusKind.ReadOnly, value, hint, Nested: true);

    /// <summary>A recipe with two ingredient slots, plus a value the card does not own.</summary>
    private static IReadOnlyList<AssetField> RecipeFields() => new[]
    {
        Field("Caption", "Garden"),
        Field("Ingredients[0].AllowedTypes[0]", "CI_Plank", "ObjectProperty"),
        Field("Ingredients[0].Amount.NoSkill", "13"),
        Field("Ingredients[0].Amount.Basic", "12"),
        Field("Ingredients[0].Amount.Medium", "11"),
        Field("Ingredients[0].Amount.Advanced", "10"),
        Field("Ingredients[0].Amount.AboveAdvanced", "9"),
        Field("Ingredients[0].Purpose", "Crafting"),
        Field("Ingredients[1].AllowedTypes[0]", "CI_Metal_Scrap", "ObjectProperty"),
        Field("Ingredients[1].Amount.NoSkill", "4"),
        Field("Ingredients[1].Amount.Basic", "4"),
        Field("Ingredients[1].Amount.Medium", "3"),
    };

    [Fact]
    public void A_recipe_shows_one_ingredient_row_per_slot()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Base Garden", RecipeFields());

        Assert.True(vm.HasIngredients);
        Assert.Equal(new[] { 0, 1 }, vm.IngredientSlots.Select(s => s.Index).ToArray());
    }

    [Fact]
    public void The_ingredient_values_leave_the_plain_field_list()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Base Garden", RecipeFields());

        // The card owns the tag and the five amounts...
        Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "Ingredients[0].AllowedTypes[0]");
        Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "Ingredients[0].Amount.NoSkill");
        // ...but a slot field the card does not show stays, and the recipe's own values stay.
        Assert.Contains(vm.Fields, f => f.TechnicalName == "Ingredients[0].Purpose");
        Assert.Contains(vm.Fields, f => f.TechnicalName == "Caption");
    }

    [Fact]
    public void An_item_has_no_ingredients_card()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Tomato", new[] { Field("Weight", "20") });

        Assert.False(vm.HasIngredients);
        Assert.Empty(vm.IngredientSlots);
    }

    [Fact]
    public void A_recipe_with_no_ingredient_slots_shows_no_card()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Odd recipe", new[] { Field("Caption", "Odd") });

        Assert.False(vm.HasIngredients);
    }

    [Fact]
    public void A_row_shows_the_accepted_tag_and_the_five_amounts()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Base Garden", RecipeFields());

        var slot = vm.IngredientSlots[0];
        Assert.Equal("CI_Plank", slot.SelectedTag!.Name);
        Assert.Equal("13", slot.Amounts.Single(a => a.Label == "No skill").Value);
        Assert.Equal(new[] { "No skill", "Basic", "Medium", "Advanced", "Above advanced" },
            slot.Amounts.Select(a => a.Label).ToArray());
        Assert.Equal("Ingredients[0].Amount.Advanced", slot.Amounts[3].TechnicalName);
    }

    [Fact]
    public void The_tag_picker_offers_the_games_real_tags()
    {
        var vm = new WorkspaceViewModel();
        var tags = new[]
        {
            new RecipeTag("CI_Plank", "Plank"),
            new RecipeTag("CI_Metal_Scrap", "Metal Scrap"),
        };
        vm.Show("Recipes", "Base Garden", RecipeFields(), ingredientTags: tags);

        Assert.Equal(2, vm.IngredientSlots[0].TagChoices.Count);
        Assert.Equal("CI_Plank", vm.IngredientSlots[0].SelectedTag!.Name);   // the file's own tag, chosen
    }

    [Fact]
    public void A_tag_the_index_does_not_have_still_shows_with_a_plain_label()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Recipes", "Base Garden", RecipeFields());   // no tag list supplied

        Assert.Equal("CI_Plank", vm.IngredientSlots[0].SelectedTag!.Name);
        Assert.Equal("Plank", vm.IngredientSlots[0].SelectedTag!.Label);
    }

    // --- Recording (the bookkeeping the card's edits leave behind) --------------------------------

    [AvaloniaFact]
    public async Task Editing_an_ingredient_amount_records_a_set_on_that_slots_path()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);

            var amount = vm.IngredientSlots[0].Amounts.Single(a => a.Label == "No skill");
            vm.BeginEditCommand.Execute(amount);
            amount.Draft = "7";
            vm.CommitEditCommand.Execute(amount);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("Ingredients[0].Amount.NoSkill", record.FieldTechnicalName);
            Assert.Equal("13", record.OldValue);
            Assert.Equal("7", record.NewValue);
            Assert.Equal(ChangeOperation.Set, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Choosing_a_tag_records_a_set_on_the_slots_tag_path()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);

            var slot = vm.IngredientSlots[0];
            slot.SelectedTag = new RecipeTag("CI_Metal_Scrap", "Metal Scrap");
            vm.SetIngredientTagCommand.Execute(slot);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("Ingredients[0].AllowedTypes[0]", record.FieldTechnicalName);
            Assert.Equal("CI_Plank", record.OldValue);
            Assert.Equal("CI_Metal_Scrap", record.NewValue);
            Assert.Equal(ChangeOperation.Set, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_project_category_is_offered_and_recorded_with_its_whole_path()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var folder = projects.Current!.FolderPath;
            var categoryPath = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod/CI_Olives";
            new NewAssetStore().Add(folder, new NewAsset(
                Guid.NewGuid(), "CI_Olives", NewAssetKind.Category, "Group Canned Vegetables",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Group_Canned_Vegetables",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod",
                DateTime.UtcNow));

            var (vm, _) = await RecipeOpened(projects);

            var choice = Assert.Single(vm.IngredientSlots[0].TagChoices.Where(t => t.Name == "CI_Olives"));
            Assert.Equal("Olives", choice.Label);

            var slot = vm.IngredientSlots[0];
            slot.SelectedTag = choice;
            vm.SetIngredientTagCommand.Execute(slot);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(folder));
            Assert.Equal(categoryPath, record.NewValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Reopening_a_recipe_shows_the_tag_that_was_chosen()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);

            var slot = vm.IngredientSlots[0];
            slot.SelectedTag = new RecipeTag("CI_Metal_Scrap", "Metal Scrap");
            vm.SetIngredientTagCommand.Execute(slot);

            await vm.OpenAsync(vm.SelectedAsset!);

            Assert.Equal("CI_Metal_Scrap", vm.IngredientSlots[0].SelectedTag!.Name);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Reopening_shows_a_project_category_by_its_plain_name()
    {
        var (projects, root) = TestProjects.With("OliveMod");
        try
        {
            var folder = projects.Current!.FolderPath;
            new NewAssetStore().Add(folder, new NewAsset(
                Guid.NewGuid(), "CI_Olives", NewAssetKind.Category, "Group Canned Vegetables",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Group_Canned_Vegetables",
                "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/SCMW_OliveMod",
                DateTime.UtcNow));

            var (vm, _) = await RecipeOpened(projects);

            var slot = vm.IngredientSlots[0];
            slot.SelectedTag = slot.TagChoices.Single(t => t.Name == "CI_Olives");
            vm.SetIngredientTagCommand.Execute(slot);

            await vm.OpenAsync(vm.SelectedAsset!);

            var shown = vm.IngredientSlots[0].SelectedTag!;
            Assert.Equal("CI_Olives", shown.Name);
            Assert.Equal("Olives", shown.Label);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Picking_the_files_own_tag_back_drops_the_change()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);

            var slot = vm.IngredientSlots[0];
            slot.SelectedTag = new RecipeTag("CI_Metal_Scrap", "Metal Scrap");
            vm.SetIngredientTagCommand.Execute(slot);

            var back = vm.IngredientSlots[0];
            back.SelectedTag = back.TagChoices.Single(t => t.Name == "CI_Plank");
            vm.SetIngredientTagCommand.Execute(back);

            Assert.DoesNotContain(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath),
                                  r => r.FieldTechnicalName == "Ingredients[0].AllowedTypes[0]");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_ingredients_card_renders_a_row_per_slot()
    {
        var (vm, source) = await RecipeOpened(null);
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();

        var card = view.FindControl<Border>("IngredientsCard");
        Assert.NotNull(card);
        Assert.True(card!.IsVisible);

        var list = view.FindControl<ItemsControl>("IngredientList");
        Assert.Equal(2, list!.ItemCount);
        Assert.Equal(1, source.SlotsRead);   // the recipe file was read once
    }

    // --- Adding and removing slots ---------------------------------------------------------------

    [AvaloniaFact]
    public async Task Adding_an_ingredient_records_an_add_entry_and_shows_a_new_row()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);
            Assert.Equal(2, vm.IngredientSlots.Count);

            vm.AddIngredientSlotCommand.Execute(null);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("Ingredients", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.AddEntry, record.Operation);

            Assert.Equal(3, vm.IngredientSlots.Count);                                 // the card shows it at once
            Assert.Equal("CI_Metal_Scrap", vm.IngredientSlots[2].SelectedTag!.Name);   // cloned from the last slot
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_an_ingredient_records_a_remove_entry_at_its_index()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);

            vm.RemoveIngredientSlotCommand.Execute(vm.IngredientSlots[0]);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("Ingredients[0]", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.RemoveEntry, record.Operation);

            var only = Assert.Single(vm.IngredientSlots);
            Assert.Equal("CI_Metal_Scrap", only.SelectedTag!.Name);   // the survivor moved up
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Reopening_a_recipe_keeps_a_removed_slot_gone()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);
            vm.RemoveIngredientSlotCommand.Execute(vm.IngredientSlots[0]);

            await vm.OpenAsync(vm.SelectedAsset!);

            var only = Assert.Single(vm.IngredientSlots);
            Assert.Equal("CI_Metal_Scrap", only.SelectedTag!.Name);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_an_ingredient_that_clears_other_changes_says_so()
    {
        var (projects, root) = TestProjects.With("Recipe mod");
        try
        {
            var (vm, _) = await RecipeOpened(projects);

            // A normal value change on the recipe, then a slot removal — which clears it.
            var amount = vm.IngredientSlots[0].Amounts.Single(a => a.Label == "No skill");
            vm.BeginEditCommand.Execute(amount);
            amount.Draft = "99";
            vm.CommitEditCommand.Execute(amount);

            vm.RemoveIngredientSlotCommand.Execute(vm.IngredientSlots[0]);

            Assert.True(vm.HasIngredientNotice);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_card_offers_a_way_to_add_and_remove()
    {
        var (vm, _) = await RecipeOpened(null);
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();

        Assert.NotNull(view.FindControl<Button>("AddIngredient"));
        Assert.Equal(2, view.GetVisualDescendants().OfType<Button>()
            .Count(b => b.Name == "RemoveIngredient"));
    }

    /// <summary>Open the fake recipe in a workspace with a chosen project; returns the view model.</summary>
    private static async Task<(WorkspaceViewModel Vm, RecipeSource Source)> RecipeOpened(
        ProjectStore? projects)
    {
        var source = new RecipeSource();
        ItemChangeStore? changes = projects is null ? null : TestProjects.ChangeStore(projects);
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source), changes, projects, new NewAssetStore());
        await vm.OpenKindAsync("Recipes");
        await vm.OpenAsync(vm.VisibleAssets.Single());
        return (vm, source);
    }

    /// <summary>A fake game index: a recipe with two slots and two real CI_ tags. No game, no disk.</summary>
    private sealed class RecipeSource : IContentSource
    {
        public const string Recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Placeables/CR_Base_Garden";
        public const string Plank = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Plank";
        public const string Scrap = "SCUM/Content/ConZ_Files/Items/Crafting/Ingredients/CI_Metal_Scrap";

        public int SlotsRead { get; private set; }

        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(Recipe, "Base Garden", "Placeables", StatusKind.Editable, 1, Recipe + ".uasset", null),
            new AssetSummary(Plank, "CI Plank", "Crafting", StatusKind.Unknown, 0, Plank + ".uasset", null),
            new AssetSummary(Scrap, "CI Metal Scrap", "Crafting", StatusKind.Unknown, 0, Scrap + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Placeables", "Crafting" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default)
        {
            if (id != Recipe) return Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
            SlotsRead++;
            return Task.FromResult(RecipeFields());
        }
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
