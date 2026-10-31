using Avalonia.Headless.XUnit;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

public class PreviewViewModelTests
{
    [AvaloniaFact] // Task 4.3: the diff lists only changed files, with before → after
    public void The_preview_lists_only_changed_files_with_before_and_after()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;

            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", id, assetPath: "/a/Duct_Tape");
            changes.RecordMultiply("Batteries", "Weight", "400", "2", id, assetPath: "/a/Batteries");

            var vm = new PreviewViewModel(changes, projects);

            Assert.Equal(2, vm.Changes.Count);   // only the two changed files, nothing untouched

            var duct = vm.Changes.Single(c => c.ItemName == "Duct Tape");
            Assert.Contains(duct.Values, v => v.Summary == "10 → 25");

            var batteries = vm.Changes.Single(c => c.ItemName == "Batteries");
            Assert.Contains(batteries.Values, v => v.Summary == "400 → 800");   // the ×2 is worked out
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // an added field reads as a sentence, not as an empty before-and-after
    public void An_added_field_reads_as_added_and_set()
    {
        var (projects, root) = TestProjects.With("Tomato mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;

            changes.RecordAddField("/a/Tomato_ES", "_autoDestructTime", "0", "/a/Stone_ES", id,
                                   assetPath: "/a/Tomato_ES");

            var vm = new PreviewViewModel(changes, projects);

            var tomato = Assert.Single(vm.Changes);
            Assert.Contains(tomato.Values, v => v.Summary == "added, set to 0");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // polish: a field added to an item's class file joins the item's other changes
    public void An_added_field_on_the_class_file_shares_the_items_card()
    {
        var (projects, root) = TestProjects.With("Watering can mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;

            // The item file (size in inventory) and its class file (where the timer lives) are one item.
            changes.RecordEdit("/a/Watering_Can_ES", "MaxStackSize", "5", "10", id,
                               assetPath: "/a/Watering_Can_ES");
            changes.RecordAddField("/a/Watering_Can", "_autoDestructTime", "30", "/a/Stone_ES", id,
                                   assetPath: "/a/Watering_Can");

            var vm = new PreviewViewModel(changes, projects);

            var can = Assert.Single(vm.Changes);            // one item, one card — not two
            Assert.Equal("/a/Watering_Can_ES", can.Path);
            Assert.Equal(2, can.Values.Count);
            Assert.Contains(can.Values, v => v.ValueName == "Max Stack");
            Assert.Contains(can.Values, v => v.ValueName == "Auto Destruct Time");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // polish: each change shows the item's friendly name and its full path
    public void The_preview_names_each_item_and_shows_its_path()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", id, assetPath: "/a/Duct_Tape_ES");

            var vm = new PreviewViewModel(changes, projects);

            var line = Assert.Single(vm.Changes);
            Assert.Equal("/a/Duct_Tape_ES", line.Path);
            Assert.Equal("Duct Tape", line.FriendlyName);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // the Build list speaks the same words the Cooking card uses, not a raw path
    public void A_cooking_change_reads_in_plain_words()
    {
        var (projects, root) = TestProjects.With("Dish mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            const string dish = "/a/Cook_Dish_Grill_Fish";

            changes.RecordEdit(dish, "OptionalIngredients[1].PossibleIngredient[0].Item", "None",
                               "/Game/ConZ_Files/Items/Food/MiscFood/BlackPepper.BlackPepper_C", id, assetPath: dish);
            changes.RecordEdit(dish, "OptionalIngredients[1].PossibleIngredient[0].IsResource", "True", "False",
                               id, assetPath: dish);
            changes.RecordListChange(dish, "OptionalIngredients[1].PossibleIngredient",
                                     ChangeOperation.AddEntry, id, assetPath: dish);

            var vm = new PreviewViewModel(changes, projects);

            var line = Assert.Single(vm.Changes);
            Assert.Contains(line.Values, v =>
                v.ValueName == "Optional ingredient 2 · food 1 · item" && v.Summary == "none → BlackPepper");
            Assert.Contains(line.Values, v =>
                v.ValueName == "Optional ingredient 2 · food 1 · is a resource" && v.Summary == "yes → no");
            Assert.Contains(line.Values, v =>
                v.ValueName == "Optional ingredient 2 · foods" && v.Summary == "added");
            Assert.Equal("3 changes", line.ChangeCountText);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // a multiply is still worked out, and its answer is shown to three places
    public void A_multiplied_value_is_shown_with_three_places()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            changes.RecordMultiply("Batteries", "Weight", "0.30000000000000004", "0.5", id, assetPath: "/a/Batteries");

            var vm = new PreviewViewModel(changes, projects);

            var line = Assert.Single(vm.Changes);
            Assert.Contains(line.Values, v => v.Summary == "0.300 → 0.150");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // one change reads "1 change", not "1 changes"
    public void A_single_change_is_counted_in_the_singular()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            changes.RecordEdit("Duct Tape", "Max stack", "10", "25", id, assetPath: "/a/Duct_Tape");

            var vm = new PreviewViewModel(changes, projects);

            Assert.Equal("1 change", Assert.Single(vm.Changes).ChangeCountText);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // polish: an item can be taken out of the mod from the preview
    public void Removing_an_item_from_the_preview_drops_its_changes()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            changes.RecordEdit("/a/Duct_Tape_ES", "MaxStackSize", "10", "25", id, assetPath: "/a/Duct_Tape_ES");
            changes.RecordEdit("/a/AK_15_ES", "Damage", "45", "60", id, assetPath: "/a/AK_15_ES");

            var vm = new PreviewViewModel(changes, projects);
            Assert.Equal(2, vm.Changes.Count);

            vm.RemoveItemCommand.Execute(vm.Changes.Single(c => c.Path == "/a/Duct_Tape_ES"));

            var left = Assert.Single(vm.Changes);
            Assert.Equal("/a/AK_15_ES", left.Path);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // polish: removing an item also drops a field added to its class file
    public void Removing_an_item_also_drops_a_field_added_to_its_class_file()
    {
        var (projects, root) = TestProjects.With("Watering can mod");
        try
        {
            var changes = TestProjects.ChangeStore(projects);
            var id = projects.Current!.Id;
            changes.RecordEdit("/a/Watering_Can_ES", "MaxStackSize", "5", "10", id,
                               assetPath: "/a/Watering_Can_ES");
            changes.RecordAddField("/a/Watering_Can", "_autoDestructTime", "30", "/a/Stone_ES", id,
                                   assetPath: "/a/Watering_Can");

            var vm = new PreviewViewModel(changes, projects);
            var can = Assert.Single(vm.Changes);

            vm.RemoveItemCommand.Execute(can);

            Assert.Empty(vm.Changes);
            Assert.Empty(changes.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact] // with nothing chosen, the preview says so instead of showing a fall-back's changes
    public void The_preview_says_so_when_no_project_is_chosen()
    {
        var (projects, root) = TestProjects.With("Tape mod");
        try
        {
            projects.SetCurrent("");
            var changes = TestProjects.ChangeStore(projects);

            var vm = new PreviewViewModel(changes, projects);

            Assert.True(vm.NeedsProject);
            Assert.Empty(vm.Changes);
            Assert.False(string.IsNullOrWhiteSpace(vm.ProjectWarning));
        }
        finally { TestProjects.Cleanup(root); }
    }
}
