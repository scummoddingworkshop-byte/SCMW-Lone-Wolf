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
/// The dish page's Ingredients card: one row per ingredient, each showing its title and the foods it
/// may take (a food points at an item and carries five values), editable like any other value. The
/// card owns those values, so they leave the plain (locked) field list.
/// </summary>
public class CookingEditorTests
{
    private const string Apple = "/Game/ConZ_Files/Items/Food/Fruit/Apple.Apple_C";
    private const string Pear = "/Game/ConZ_Files/Items/Food/Fruit/Pear.Pear_C";

    private static AssetField Field(string technical, string value, string hint = "IntProperty") =>
        new(technical, technical, StatusKind.ReadOnly, value, hint, Nested: true);

    /// <summary>A dish with one main ingredient (Base, food Apple) and one optional (Topping, food Pear).</summary>
    private static IReadOnlyList<AssetField> DishFields() => new[]
    {
        Field("CookingTime", "30"),
        Field("MainIngredients[0].IngredientTitle", "Base"),
        Field("MainIngredients[0].PossibleIngredient[0].Item", Apple, "SoftObjectProperty"),
        Field("MainIngredients[0].PossibleIngredient[0].Liters", "0.5", "FloatProperty"),
        Field("MainIngredients[0].PossibleIngredient[0].Usage", "1"),
        Field("MainIngredients[0].PossibleIngredient[0].NutrientInclusionFactor", "1.0", "FloatProperty"),
        Field("MainIngredients[0].PossibleIngredient[0].ShouldConsumeWhole", "False", "BoolProperty"),
        Field("MainIngredients[0].PossibleIngredient[0].IsResource", "False", "BoolProperty"),
        Field("MainIngredients[0].PossibleIngredient[0].ResourceType", "/Game/…/Some.Type", "ObjectProperty"),
        Field("OptionalIngredients[0].IngredientTitle", "Topping"),
        Field("OptionalIngredients[0].PossibleIngredient[0].Item", Pear, "SoftObjectProperty"),
    };

    private static IReadOnlyList<CookingFood> TwoFoods() => new[]
    {
        new CookingFood(Apple, "Apple"),
        new CookingFood(Pear, "Pear"),
    };

    [Fact]
    public void A_dish_shows_one_row_per_ingredient()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Cake ApplePie", DishFields());

        Assert.True(vm.HasCookingIngredients);
        Assert.Equal(2, vm.CookingSlots.Count);
        Assert.True(vm.CookingSlots[0].IsMain);
        Assert.False(vm.CookingSlots[1].IsMain);
    }

    [Fact]
    public void The_cooking_values_leave_the_plain_field_list()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Cake ApplePie", DishFields());

        // The card owns the title, the food's item and its values...
        Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "MainIngredients[0].IngredientTitle");
        Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "MainIngredients[0].PossibleIngredient[0].Item");
        Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "MainIngredients[0].PossibleIngredient[0].Liters");
        // ...but a field the card does not own stays, and the dish's own numbers stay.
        Assert.DoesNotContain(vm.Fields, f => f.TechnicalName == "MainIngredients[0].PossibleIngredient[0].ResourceType");
        Assert.Contains(vm.Fields, f => f.TechnicalName == "CookingTime");
    }

    [Fact]
    public void A_resource_food_starts_with_no_item_chosen()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Cake Bread", new[]
        {
            Field("MainIngredients[0].IngredientTitle", "Milk"),
            Field("MainIngredients[0].PossibleIngredient[0].Item", "None", "SoftObjectProperty"),
            Field("MainIngredients[0].PossibleIngredient[0].ResourceType", "Milk_C", "ObjectProperty"),
            Field("MainIngredients[0].PossibleIngredient[0].Liters", "0.1", "FloatProperty"),
        });

        var food = Assert.Single(vm.CookingSlots[0].Foods);
        Assert.False(food.HasItem);
        Assert.True(food.IsResource);
        Assert.Equal("Milk", food.ResourceLabel);
        Assert.Null(food.SelectedFood);   // nothing chosen yet; picking an item is how it changes
    }

    [Fact]
    public void An_item_has_no_cooking_card()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Tomato", new[] { Field("Weight", "20") });

        Assert.False(vm.HasCookingIngredients);
        Assert.Empty(vm.CookingSlots);
    }

    [Fact]
    public void A_dish_with_no_ingredients_shows_no_card()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Odd dish", new[] { Field("CookingTime", "5") });

        Assert.False(vm.HasCookingIngredients);
    }

    [Fact]
    public void A_row_shows_the_title_and_its_foods()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Cake ApplePie", DishFields(), cookingFoods: TwoFoods());

        var ingredient = vm.CookingSlots[0];
        Assert.Equal("Base", ingredient.Title!.Value);
        Assert.Equal("Main ingredient 1", ingredient.Header);

        var food = Assert.Single(ingredient.Foods);
        Assert.Equal(Apple, food.SelectedFood!.SoftPath);
        Assert.Equal(new[] { "Liters", "Usage", "Nutrient inclusion", "Consume whole", "Is resource" },
            food.Values.Select(v => v.Label).ToArray());
        Assert.Equal("MainIngredients[0].PossibleIngredient[0].Liters", food.Values[0].TechnicalName);
    }

    [Fact]
    public void The_food_picker_offers_the_games_foods()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Cake ApplePie", DishFields(), cookingFoods: TwoFoods());

        Assert.Equal(2, vm.CookingSlots[0].Foods[0].Choices.Count);
        Assert.Equal(Apple, vm.CookingSlots[0].Foods[0].SelectedFood!.SoftPath);
    }

    [Fact]
    public void A_food_the_index_does_not_have_still_shows_with_a_plain_label()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Cake ApplePie", DishFields());   // no food list supplied

        Assert.Equal(Apple, vm.CookingSlots[0].Foods[0].SelectedFood!.SoftPath);
        Assert.Equal("Apple", vm.CookingSlots[0].Foods[0].SelectedFood!.Label);
    }

    // --- Recording (the bookkeeping the card's edits leave behind) --------------------------------

    [AvaloniaFact]
    public async Task Editing_a_food_value_records_a_set_on_that_path()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            var liters = vm.CookingSlots[0].Foods[0].Values.Single(v => v.Label == "Liters");
            vm.BeginEditCommand.Execute(liters);
            liters.Draft = "0.8";
            vm.CommitEditCommand.Execute(liters);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("MainIngredients[0].PossibleIngredient[0].Liters", record.FieldTechnicalName);
            Assert.Equal("0.5", record.OldValue);
            Assert.Equal("0.8", record.NewValue);
            Assert.Equal(ChangeOperation.Set, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Choosing_a_food_records_a_set_on_the_item_path()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C", "Banana");
            vm.SetCookingFoodCommand.Execute(food);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("MainIngredients[0].PossibleIngredient[0].Item", record.FieldTechnicalName);
            Assert.Equal(Apple, record.OldValue);
            Assert.Equal("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C", record.NewValue);
            Assert.Equal(ChangeOperation.Set, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Reopening_a_dish_shows_the_recorded_food_in_the_picker()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C", "Banana");
            vm.SetCookingFoodCommand.Execute(food);

            await vm.OpenAsync(vm.SelectedAsset!);

            // The picker must show the project's food, matching the row's values — not the file's.
            Assert.Equal("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C",
                         vm.CookingSlots[0].Foods[0].SelectedFood!.SoftPath);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Picking_the_original_food_back_drops_the_change()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C", "Banana");
            vm.SetCookingFoodCommand.Execute(food);

            food.SelectedFood = new CookingFood(Apple, "Apple");   // back to the file's own item
            vm.SetCookingFoodCommand.Execute(food);

            Assert.Empty(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Re_picking_the_same_food_does_not_record_twice()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood("/Game/ConZ_Files/Items/Food/Fruit/Banana.Banana_C", "Banana");
            vm.SetCookingFoodCommand.Execute(food);
            vm.SetCookingFoodCommand.Execute(food);   // the picker settling on the same food again

            Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    // --- Adding and removing ---------------------------------------------------------------

    [AvaloniaFact]
    public async Task Adding_a_food_records_an_add_entry_on_the_ingredients_food_list()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            vm.AddCookingFoodCommand.Execute(vm.CookingSlots[0]);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("MainIngredients[0].PossibleIngredient", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.AddEntry, record.Operation);
            Assert.Equal(2, vm.CookingSlots[0].Foods.Count);                         // shown at once
            Assert.Equal(Apple, vm.CookingSlots[0].Foods[1].SelectedFood!.SoftPath);  // cloned from the last
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_a_food_records_a_remove_entry_at_its_index()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            vm.RemoveCookingFoodCommand.Execute(vm.CookingSlots[0].Foods[0]);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("MainIngredients[0].PossibleIngredient[0]", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.RemoveEntry, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Adding_a_main_ingredient_records_an_add_entry_on_the_main_list()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            vm.AddCookingMainIngredientCommand.Execute(null);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("MainIngredients", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.AddEntry, record.Operation);
            Assert.Equal(3, vm.CookingSlots.Count);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Adding_an_optional_ingredient_records_an_add_entry_on_the_optional_list()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            vm.AddCookingOptionalIngredientCommand.Execute(null);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("OptionalIngredients", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.AddEntry, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Removing_an_ingredient_records_a_remove_entry_at_its_path()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            vm.RemoveCookingIngredientCommand.Execute(vm.CookingSlots[0]);

            var record = Assert.Single(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            Assert.Equal("MainIngredients[0]", record.FieldTechnicalName);
            Assert.Equal(ChangeOperation.RemoveEntry, record.Operation);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Reopening_a_dish_keeps_a_removed_food_gone()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await DishOpened(projects);

            vm.RemoveCookingFoodCommand.Execute(vm.CookingSlots[0].Foods[0]);
            await vm.OpenAsync(vm.SelectedAsset!);

            Assert.Empty(vm.CookingSlots[0].Foods);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_card_renders_a_row_per_ingredient_with_add_and_remove()
    {
        var (vm, source) = await DishOpened(null);
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();

        var card = view.FindControl<Border>("CookingCard");
        Assert.NotNull(card);
        Assert.True(card!.IsVisible);

        var list = view.FindControl<ItemsControl>("CookingIngredientList");
        Assert.Equal(2, list!.ItemCount);
        Assert.Equal(1, source.DishRead);   // the dish file was read once

        Assert.NotNull(view.FindControl<Button>("AddCookingMainIngredient"));
        Assert.NotNull(view.FindControl<Button>("AddCookingOptionalIngredient"));
        Assert.Equal(2, view.GetVisualDescendants().OfType<Button>()
            .Count(b => b.Name == "RemoveCookingIngredient"));
    }

    // --- A resource food (oil, milk, water) switched to a real item ----------------------------

    private const string Oil = "/Game/ConZ_Files/GameResources/Food/Liquids/Oil.Oil_C";
    private const string Pepper = "/Game/ConZ_Files/Items/Food/MiscFood/BlackPepper.BlackPepper_C";

    /// <summary>A dish whose only ingredient is a fluid ("resource"): oil, exactly like Dish Grill Fish.</summary>
    private static IReadOnlyList<AssetField> OilDishFields() => new[]
    {
        Field("CookingTime", "150", "FloatProperty"),
        Field("OptionalIngredients[0].IngredientTitle", "Ingredient_Oil"),
        Field("OptionalIngredients[0].IsResource", "True", "BoolProperty"),
        Field("OptionalIngredients[0].PossibleIngredient[0].Item", "None", "SoftObjectProperty"),
        Field("OptionalIngredients[0].PossibleIngredient[0].ResourceType", "Oil_C", "ObjectProperty"),
        Field("OptionalIngredients[0].PossibleIngredient[0].IsResource", "True", "BoolProperty"),
        Field("OptionalIngredients[0].PossibleIngredient[0].Liters", "0.1", "FloatProperty"),
    };

    private static async Task<(WorkspaceViewModel Vm, DishSource Source)> OilDishOpened(ProjectStore? projects)
    {
        var source = new DishSource(OilDishFields());
        ItemChangeStore? changes = projects is null ? null : TestProjects.ChangeStore(projects);
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source), changes, projects);
        await vm.OpenKindAsync("Cooking");
        await vm.OpenAsync(vm.VisibleAssets.Single());
        return (vm, source);
    }

    [Fact]
    public void A_resource_food_shows_the_picker_and_its_resource_name()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Grill Fish", OilDishFields(), cookingFoods: TwoFoods());

        var food = vm.CookingSlots[0].Foods[0];
        Assert.True(food.IsResource);
        Assert.Equal("Oil", food.ResourceLabel);
        Assert.Equal(2, food.Choices.Count);          // the game's foods are offered, so one can be picked
        Assert.False(food.CanResetToResource);        // nothing to go back to yet
    }

    [AvaloniaFact]
    public async Task Picking_an_item_on_a_resource_food_records_the_switch()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await OilDishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood(Pepper, "BlackPepper");
            vm.SetCookingFoodCommand.Execute(food);

            var records = TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath);
            Assert.Equal(3, records.Count);
            Assert.Contains(records, r =>
                r.FieldTechnicalName == "OptionalIngredients[0].PossibleIngredient[0].Item"
                && r.OldValue == "None" && r.NewValue == Pepper);
            Assert.Contains(records, r =>
                r.FieldTechnicalName == "OptionalIngredients[0].PossibleIngredient[0].IsResource"
                && r.OldValue == "True" && r.NewValue == "False");
            Assert.Contains(records, r =>
                r.FieldTechnicalName == "OptionalIngredients[0].IsResource"
                && r.OldValue == "True" && r.NewValue == "False");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_switched_resource_shows_the_item_and_offers_the_way_back()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await OilDishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood(Pepper, "BlackPepper");
            vm.SetCookingFoodCommand.Execute(food);

            var shown = vm.CookingSlots[0].Foods[0];
            Assert.False(shown.IsResource);
            Assert.Equal(Pepper, shown.SelectedFood!.SoftPath);
            Assert.True(shown.CanResetToResource);
            Assert.Equal("Oil", shown.ResourceLabel);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Going_back_to_the_resource_drops_all_three_changes()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await OilDishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood(Pepper, "BlackPepper");
            vm.SetCookingFoodCommand.Execute(food);

            vm.ResetCookingResourceCommand.Execute(vm.CookingSlots[0].Foods[0]);

            Assert.Empty(TestProjects.ChangeStore(projects).For(projects.Current!.FolderPath));
            var back = vm.CookingSlots[0].Foods[0];
            Assert.True(back.IsResource);
            Assert.False(back.CanResetToResource);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Reopening_a_dish_shows_the_item_a_resource_was_switched_to()
    {
        var (projects, root) = TestProjects.With("Cooking mod");
        try
        {
            var (vm, _) = await OilDishOpened(projects);

            var food = vm.CookingSlots[0].Foods[0];
            food.SelectedFood = new CookingFood(Pepper, "BlackPepper");
            vm.SetCookingFoodCommand.Execute(food);

            await vm.OpenAsync(vm.SelectedAsset!);

            var shown = vm.CookingSlots[0].Foods[0];
            Assert.Equal(Pepper, shown.SelectedFood!.SoftPath);
            Assert.True(shown.CanResetToResource);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_card_shows_a_picker_on_a_resource_row()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Cooking", "Dish Grill Fish", OilDishFields(), cookingFoods: TwoFoods());

        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();

        // The picker is there on a resource row (that is how a resource becomes an item), and the
        // way back only appears once something has been picked.
        var pickers = view.GetVisualDescendants().OfType<SearchPicker>().ToList();
        Assert.Single(pickers);
        Assert.True(pickers[0].IsVisible);
        Assert.Empty(view.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "ResetCookingResource" && b.IsVisible));
    }

    /// <summary>Open the fake dish in a workspace with a chosen project; returns the view model.</summary>
    private static async Task<(WorkspaceViewModel Vm, DishSource Source)> DishOpened(ProjectStore? projects)
    {
        var source = new DishSource();
        ItemChangeStore? changes = projects is null ? null : TestProjects.ChangeStore(projects);
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source,
                                        new ContentCatalog(source), changes, projects);
        await vm.OpenKindAsync("Cooking");
        await vm.OpenAsync(vm.VisibleAssets.Single());
        return (vm, source);
    }

    /// <summary>A fake game index: one dish with two ingredients, plus the two food items. No game, no disk.</summary>
    private sealed class DishSource : IContentSource
    {
        public const string Dish = "SCUM/Content/ConZ_Files/Cooking/Recipes/Cakes/Cook_Dish_Cake_ApplePie";
        public const string AppleItem = "SCUM/Content/ConZ_Files/Items/Food/Fruit/Apple_ES";
        public const string PearItem = "SCUM/Content/ConZ_Files/Items/Food/Fruit/Pear_ES";

        /// <summary>A source for another dish's fields (e.g. the oil dish), same index.</summary>
        private readonly IReadOnlyList<AssetField> _fields;

        public DishSource(IReadOnlyList<AssetField>? fields = null) => _fields = fields ?? DishFields();

        public int DishRead { get; private set; }

        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            new AssetSummary(Dish, "Dish Cake ApplePie", "Cakes", StatusKind.Editable, 1, Dish + ".uasset", null),
            new AssetSummary(AppleItem, "Apple", "Food", StatusKind.Unknown, 0, AppleItem + ".uasset", null),
            new AssetSummary(PearItem, "Pear", "Food", StatusKind.Unknown, 0, PearItem + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Cakes", "Food" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default)
        {
            if (id != Dish) return Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
            DishRead++;
            return Task.FromResult(_fields);
        }
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
