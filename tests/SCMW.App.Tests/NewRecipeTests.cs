using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// Creating a brand-new recipe from the Workspace: type a plain name, start from a game recipe, and
/// the app records it and shows it in the list ready to edit (Task 7.5).
/// </summary>
public class NewRecipeTests
{
    /// <summary>A fake game index with one recipe, read as a Weight field. No game, no disk.</summary>
    private sealed class RecipeSource : IContentSource
    {
        public const string Recipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Wooden_Arrow";
        public const string OtherRecipe = "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/CR_Stone_Axe";
        public const string Item = "SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil_ES";

        private static readonly IReadOnlyList<AssetSummary> Rows = new[]
        {
            // A recipe's friendly "Name" differs from its real object name (the path's last part).
            new AssetSummary(Recipe, "Wooden Arrow", "Items", StatusKind.Editable, 1, Recipe + ".uasset", null),
            new AssetSummary(OtherRecipe, "Stone Axe", "Items", StatusKind.Editable, 1, OtherRecipe + ".uasset", null),
            // An item: the list shows the "_ES" file; the item package is its class file.
            new AssetSummary(Item, "Vegetable Oil", "Food", StatusKind.Editable, 1, Item + ".uasset", null),
        };

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "1");
        public ScanInfo? LastScan => null;
        public IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => new[] { "Items" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(s >= Rows.Count ? Array.Empty<AssetSummary>() : Rows.Skip(s).Take(t).ToList());
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(new[]
            {
                new AssetField("ProductQuantity", "ProductQuantity", StatusKind.Editable, "1"),
                // What the donor recipe makes — the value a new recipe inherits until it is re-pointed.
                new AssetField("Product", "Product", StatusKind.ReadOnly,
                    "/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil_C"),
            });
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (WorkspaceViewModel Vm, NewAssetStore Assets, ItemChangeStore Changes, ProjectStore Projects, string Root)
        Ready(RecipeSource source)
    {
        var (projects, root) = TestProjects.With("Olive Press Mod");
        var changes = TestProjects.ChangeStore(projects);
        var newAssets = new NewAssetStore();
        var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source),
                                        changes: changes, projects: projects, newAssets: newAssets);
        return (vm, newAssets, changes, projects, root);
    }

    private static void Make(WorkspaceViewModel vm, string name) =>
        (vm.NewRecipeName, vm.NewRecipeDonor) =
            (name, vm.RecipeDonors.Single(d => d.Name == "Wooden Arrow"));

    [AvaloniaFact] // the "New" panel is offered for recipes and items, and nowhere else
    public async Task The_new_panel_is_offered_for_recipes_and_items_only()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            Assert.True(vm.ShowNewRecipeButton);
            Assert.Equal("＋ New recipe", vm.NewButtonText);

            await vm.OpenKindAsync("Items");
            Assert.True(vm.ShowNewRecipeButton);
            Assert.Equal("＋ New item", vm.NewButtonText);

            await vm.OpenKindAsync("Base building");
            Assert.False(vm.ShowNewRecipeButton);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // an item comes from the Items list: its donor is the ITEM, and it records both files
    public async Task A_new_item_is_recorded_from_the_items_list()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Items");
            vm.OpenNewRecipeCommand.Execute(null);

            var donor = vm.RecipeDonors.Single(d => d.Name == "VegetableOil");
            // The donor is the item package behind the "_ES" file the list shows (the twin is derived).
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Food/MiscFood/VegetableOil", donor.Path);

            vm.NewRecipeName = "Olive Oil";
            vm.NewRecipeDonor = donor;
            vm.CreateNewRecipeCommand.Execute(null);

            var saved = Assert.Single(assets.Load(projects.Current!.FolderPath));
            Assert.Equal("OliveOil", saved.Name);
            Assert.Equal(NewAssetKind.Item, saved.Kind);
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePressMod/OliveOil", saved.PackagePath);
            // The owner's edits aim at the item's "_ES" twin, which is also what the row reads from.
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePressMod/OliveOil_ES", saved.EditPath);

            // The list row reads plainly ("Olive Oil"), even though the asset name has no space.
            var row = vm.VisibleAssets.Single(a => a.Name == "Olive Oil");
            Assert.Equal(saved.EditPath, row.AssetId);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_new_recipe_is_recorded_and_shows_in_the_list()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");

            vm.OpenNewRecipeCommand.Execute(null);
            Assert.True(vm.ShowNewRecipe);
            Assert.Contains(vm.RecipeDonors, d => d.Name == "Wooden Arrow");

            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            var saved = Assert.Single(assets.Load(projects.Current!.FolderPath));
            Assert.Equal("CR_OlivePress", saved.Name);
            Assert.Equal("Wooden Arrow", saved.DonorName);      // the shown name — display only
            Assert.Equal(RecipeSource.Recipe, saved.DonorPath); // the exact path IS the donor's identity
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePressMod", saved.Folder);

            Assert.Contains(vm.VisibleAssets, a => a.Name == "CR_OlivePress");
            Assert.False(vm.ShowNewRecipe);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_new_recipe_reads_the_donors_fields_and_saves_its_edits_against_its_own_path()
    {
        var (vm, _, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            var row = vm.VisibleAssets.Single(a => a.Name == "CR_OlivePress");
            await vm.OpenAsync(row);

            Assert.Equal("CR_OlivePress", vm.Title);
            Assert.Contains(vm.Fields, f => f.TechnicalName == "ProductQuantity");   // read from the donor

            var quantity = vm.Fields.Single(f => f.TechnicalName == "ProductQuantity");
            vm.BeginEditCommand.Execute(quantity);
            quantity.Draft = "9";
            vm.CommitEditCommand.Execute(quantity);

            var record = Assert.Single(changes.For(projects.Current!.FolderPath));
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePressMod/CR_OlivePress",
                record.AssetPath);
            Assert.Equal("9", record.NewValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_name_that_is_already_taken_is_refused_plainly()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");

            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Single(assets.Load(projects.Current!.FolderPath));   // still just the first one
            Assert.True(vm.HasNewRecipeNotice);
            Assert.Contains("already", vm.NewRecipeNotice, StringComparison.OrdinalIgnoreCase);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_new_recipe_with_no_name_or_donor_is_refused()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);

            vm.NewRecipeName = "   ";
            vm.NewRecipeDonor = vm.RecipeDonors.First();
            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Empty(assets.Load(projects.Current!.FolderPath));
            Assert.True(vm.HasNewRecipeNotice);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task A_new_recipe_with_no_project_is_refused_and_explained()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            projects.SetCurrent("");
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");

            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Empty(assets.Load(projects.Current!.FolderPath));
            Assert.True(vm.HasNewRecipeNotice);
            Assert.Contains("project", vm.NewRecipeNotice, StringComparison.OrdinalIgnoreCase);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // a new recipe must never be offered as a donor (the game has no such file to copy)
    public async Task A_new_recipe_is_never_offered_as_a_donor()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            vm.OpenNewRecipeCommand.Execute(null);

            Assert.DoesNotContain(vm.RecipeDonors,
                d => string.Equals(d.Name, "CR_OlivePress", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(vm.RecipeDonors, d => d.Name == "Wooden Arrow");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // a donor that is not a game recipe (a stale list) is refused, so nothing bad is recorded
    public async Task A_donor_that_is_not_a_game_recipe_is_refused()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            vm.NewRecipeName = "Olive Press";
            vm.NewRecipeDonor = new DonorChoice("Batteries", "SCUM/Content/ConZ_Files/Items/Equipment/Batteries_ES");

            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Empty(assets.Load(projects.Current!.FolderPath));
            Assert.True(vm.HasNewRecipeNotice);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // a name the project already recorded is refused, even when the on-screen list is stale
    public async Task A_name_the_project_already_recorded_is_refused_even_if_the_list_is_stale()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");   // the list shows only the game's own recipe

            // Another session (or a project switch) already recorded this name on disk.
            assets.Add(projects.Current!.FolderPath, new NewAsset(
                Guid.NewGuid(), "CR_OlivePress", NewAssetKind.Recipe, "CR_Wooden_Arrow", RecipeSource.Recipe,
                "SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePressMod", DateTime.UtcNow));

            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Single(assets.Load(projects.Current!.FolderPath));   // still just the pre-existing one
            Assert.True(vm.HasNewRecipeNotice);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // switching project must not leave the other project's new recipe on screen (build-breaking)
    public async Task Switching_project_leaves_another_projects_new_recipe_out_of_the_list()
    {
        var (projects, root) = TestProjects.With("Crafting Recipes", "Test Olive Press");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var newAssets = new NewAssetStore();
            var source = new RecipeSource();
            var vm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), source, new ContentCatalog(source),
                                            changes: changes, projects: projects, newAssets: newAssets);

            projects.SetCurrent(projects.All().Single(p => p.Name == "Crafting Recipes").Id);
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Contains(vm.VisibleAssets, a => a.Name == "CR_OlivePress");

            // The owner now makes and picks a different project.
            projects.SetCurrent(projects.All().Single(p => p.Name == "Test Olive Press").Id);

            Assert.DoesNotContain(vm.VisibleAssets, a => a.Name == "CR_OlivePress");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // a brand-new recipe can be deleted, and its edits go with it (no orphan records left)
    public async Task A_new_recipe_can_be_removed_with_its_changes()
    {
        var (vm, assets, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            var row = vm.VisibleAssets.Single(a => a.Name == "CR_OlivePress");
            await vm.OpenAsync(row);
            Assert.True(vm.IsOpenNewRecipe);

            var quantity = vm.Fields.Single(f => f.TechnicalName == "ProductQuantity");
            vm.BeginEditCommand.Execute(quantity);
            quantity.Draft = "9";
            vm.CommitEditCommand.Execute(quantity);
            Assert.Single(changes.For(projects.Current!.FolderPath));

            Assert.False(vm.ConfirmingRemoveNewRecipe);
            vm.RequestRemoveNewRecipeCommand.Execute(null);
            Assert.True(vm.ConfirmingRemoveNewRecipe);

            vm.ConfirmRemoveNewRecipeCommand.Execute(null);

            Assert.Empty(assets.Load(projects.Current!.FolderPath));       // the record is gone
            Assert.Empty(changes.For(projects.Current!.FolderPath));       // and its edits with it
            Assert.DoesNotContain(vm.VisibleAssets, a => a.Name == "CR_OlivePress");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Cancelling_a_removal_keeps_the_new_recipe()
    {
        var (vm, assets, _, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "CR_OlivePress"));

            vm.RequestRemoveNewRecipeCommand.Execute(null);
            vm.CancelRemoveNewRecipeCommand.Execute(null);

            Assert.False(vm.ConfirmingRemoveNewRecipe);
            Assert.Single(assets.Load(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // an ordinary game recipe is never offered for removal — only this project's new ones
    public async Task A_game_recipe_is_not_offered_for_removal()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "Wooden Arrow"));

            Assert.False(vm.IsOpenNewRecipe);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_new_recipe_button_and_panel_appear_in_the_workspace()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();

            await vm.OpenKindAsync("Recipes");
            window.UpdateLayout();

            var button = view.FindControl<Button>("StartMakeNew");
            Assert.NotNull(button);
            Assert.True(button!.IsVisible);

            vm.StartMakeNewCommand.Execute(null);
            await Task.Delay(100);   // the command switches list first when needed
            window.UpdateLayout();

            var card = view.FindControl<Border>("NewRecipeCard");
            Assert.NotNull(card);
            Assert.True(card!.IsVisible);
            Assert.NotNull(view.FindControl<TextBox>("NewRecipeNameBox"));
            Assert.NotNull(view.FindControl<Button>("CreateNewRecipe"));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // the "start from" list must open again after it has been used once (the owner's bug)
    public async Task The_donor_list_opens_again_after_a_first_pick()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();

            await vm.OpenKindAsync("Recipes");

            // First use: open the list and pick a recipe through it (like the owner).
            vm.OpenNewRecipeCommand.Execute(null);
            window.UpdateLayout();
            var picker = view.GetVisualDescendants().OfType<SearchPicker>()
                .Single(c => c.Name == "NewRecipeDonorPicker");
            picker.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(picker.FindControl<Popup>("Pop")!.IsOpen);

            picker.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
            window.UpdateLayout();

            vm.NewRecipeName = "First";
            vm.CreateNewRecipeCommand.Execute(null);
            window.UpdateLayout();   // let the panel's host drop the old dropdown

            // Second use (a new recipe): the list must open again.
            vm.OpenNewRecipeCommand.Execute(null);
            window.UpdateLayout();
            var picker2 = view.GetVisualDescendants().OfType<SearchPicker>()
                .Single(c => c.Name == "NewRecipeDonorPicker");
            Assert.NotSame(picker, picker2);   // the dropdown is rebuilt per open
            picker2.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Assert.True(picker2.FindControl<Popup>("Pop")!.IsOpen);   // <- the owner's report: no list the 2nd time
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // after making one recipe, the "start from" dropdown still offers (and takes) another
    public async Task Starting_a_second_recipe_offers_and_accepts_a_different_donor()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();

            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "First");
            vm.CreateNewRecipeCommand.Execute(null);

            vm.OpenNewRecipeCommand.Execute(null);   // open the panel a second time
            window.UpdateLayout();

            var picker = view.GetVisualDescendants().OfType<SearchPicker>()
                .Single(c => c.Name == "NewRecipeDonorPicker");
            picker.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            var options = picker.GetLogicalDescendants().OfType<ListBox>().Single();
            Assert.Equal(2, options.ItemCount);          // both recipes are offered to start from

            options.SelectedIndex = 1;                   // the owner picks the other one
            Assert.NotNull(picker.SelectedItem);
            Assert.Equal("Stone Axe", ((DonorChoice)picker.SelectedItem!).Name);
            Assert.Equal("Stone Axe", vm.NewRecipeDonor?.Name);
        }
        finally { TestProjects.Cleanup(root); }
    }

    // --- What a new recipe makes (Phase 8.2) ----------------------------------------------------

    [AvaloniaFact] // the "Makes" picker offers the game's items, and the pick is recorded on the new recipe
    public async Task A_new_recipe_can_be_pointed_at_the_item_it_makes()
    {
        var (vm, _, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);

            var item = vm.ProductChoices.Single(c => c.Label == "VegetableOil");
            Assert.Equal("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil_C", item.SoftPath);

            Make(vm, "Olive Press");
            vm.NewRecipeProduct = item;
            vm.CreateNewRecipeCommand.Execute(null);

            var record = Assert.Single(changes.For(projects.Current!.FolderPath),
                                       r => r.FieldTechnicalName == "Product");
            Assert.Equal("/Game/ConZ_Files/Items/Food/MiscFood/VegetableOil.VegetableOil_C", record.NewValue);
            Assert.Equal("SCUM/Content/ConZ_Files/Items/Crafting/Recipes/Items/SCMW_OlivePressMod/CR_OlivePress",
                         record.AssetPath);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // leaving "Makes" alone keeps the donor's own product — nothing is recorded
    public async Task A_new_recipe_left_as_is_records_no_product()
    {
        var (vm, _, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);

            Assert.Empty(changes.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // the project's own new item is offered as a "Makes" choice too
    public async Task A_projects_own_new_item_is_offered_as_a_makes_choice()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            // Make a new item of our own first (Olive Oil, from the game's vegetable oil).
            await vm.OpenKindAsync("Items");
            vm.OpenNewRecipeCommand.Execute(null);
            vm.NewRecipeName = "Olive Oil";
            vm.NewRecipeDonor = vm.RecipeDonors.Single(d => d.Name == "VegetableOil");
            vm.CreateNewRecipeCommand.Execute(null);

            // Its own item now shows in the recipe panel's "Makes" list.
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);

            var choice = vm.ProductChoices
                .Single(c => c.SoftPath == "/Game/ConZ_Files/Items/Food/MiscFood/SCMW_OlivePressMod/OliveOil.OliveOil_C");
            Assert.Equal("OliveOil", choice.Label);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // an open new recipe can change what it makes, and revert to the donor's own product
    public async Task An_open_new_recipe_can_change_what_it_makes()
    {
        var (vm, _, changes, projects, root) = Ready(new RecipeSource());
        try
        {
            // A new item of our own, so there are two products to choose between.
            await vm.OpenKindAsync("Items");
            vm.OpenNewRecipeCommand.Execute(null);
            vm.NewRecipeName = "Olive Oil";
            vm.NewRecipeDonor = vm.RecipeDonors.Single(d => d.Name == "VegetableOil");
            vm.CreateNewRecipeCommand.Execute(null);

            // A new recipe made WITHOUT picking "Makes" — it starts on the donor's own product.
            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Oil");
            vm.CreateNewRecipeCommand.Execute(null);
            var row = vm.VisibleAssets.Single(a => a.Name == "CR_Oil");
            await vm.OpenAsync(row);

            Assert.True(vm.ShowOpenMakes);
            var donorProduct = vm.OpenProductChoices.Single(c => c.Label == "VegetableOil");
            Assert.Equal(donorProduct, vm.OpenRecipeProduct);   // what the donor makes, shown

            var oliveOil = vm.OpenProductChoices.Single(c => c.Label == "OliveOil");
            vm.OpenRecipeProduct = oliveOil;

            var record = Assert.Single(changes.For(projects.Current!.FolderPath),
                                       r => r.FieldTechnicalName == "Product");
            Assert.Equal(oliveOil.SoftPath, record.NewValue);

            // Back to the donor's own product: the change is dropped, never left as a no-op.
            vm.OpenRecipeProduct = donorProduct;
            Assert.DoesNotContain(changes.For(projects.Current!.FolderPath),
                                  r => r.FieldTechnicalName == "Product");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // the open recipe shows a working "Makes" box
    public async Task The_makes_box_shows_on_an_open_new_recipe()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();

            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Oil");
            vm.CreateNewRecipeCommand.Execute(null);
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "CR_Oil"));
            window.UpdateLayout();

            var card = view.FindControl<Border>("OpenMakesCard");
            Assert.NotNull(card);
            Assert.True(card!.IsVisible);

            var picker = view.GetVisualDescendants().OfType<SearchPicker>()
                .Single(c => c.Name == "OpenMakesPicker");
            picker.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(picker.FindControl<Popup>("Pop")!.IsOpen);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // the "Makes" picker shows for a new recipe and its list opens with the game's items
    public async Task The_makes_picker_shows_for_a_new_recipe()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();

            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            window.UpdateLayout();

            var host = view.FindControl<ContentControl>("NewRecipeProductHost");
            Assert.NotNull(host);
            Assert.NotNull(host!.Content);

            var picker = host.GetVisualDescendants().OfType<SearchPicker>()
                .Single(c => c.Name == "NewRecipeProductPicker");
            picker.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Assert.True(picker.FindControl<Popup>("Pop")!.IsOpen);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task The_remove_button_shows_when_a_new_recipe_is_open()
    {
        var (vm, _, _, _, root) = Ready(new RecipeSource());
        try
        {
            var view = new WorkspaceView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();

            await vm.OpenKindAsync("Recipes");
            vm.OpenNewRecipeCommand.Execute(null);
            Make(vm, "Olive Press");
            vm.CreateNewRecipeCommand.Execute(null);
            await vm.OpenAsync(vm.VisibleAssets.Single(a => a.Name == "CR_OlivePress"));
            window.UpdateLayout();

            var bar = view.FindControl<Border>("NewRecipeBar");
            Assert.NotNull(bar);
            Assert.True(bar!.IsVisible);
            Assert.NotNull(view.FindControl<Button>("RemoveNewRecipe"));
        }
        finally { TestProjects.Cleanup(root); }
    }
}
