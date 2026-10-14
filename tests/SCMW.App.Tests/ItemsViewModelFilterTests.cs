using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ItemsViewModelFilterTests
{
    private static ItemsViewModel NewVm() => new();

    [AvaloniaFact]
    public void Starts_with_the_sample_catalogue_and_sensible_defaults()
    {
        var vm = NewVm();

        Assert.True(vm.HasItems);
        Assert.NotEmpty(vm.VisibleItems);
        Assert.Equal("All", vm.SelectedCategory);
        Assert.Contains("All", vm.Categories);
        Assert.Contains("Weapons", vm.Categories);
    }

    [AvaloniaFact]
    public void Search_filters_by_name_and_can_find_nothing()
    {
        var vm = NewVm();

        vm.Search = "duct";
        Assert.NotEmpty(vm.VisibleItems);
        Assert.All(vm.VisibleItems, i => Assert.Contains("duct", i.Name, System.StringComparison.OrdinalIgnoreCase));

        vm.Search = "zzz-no-match";
        Assert.Empty(vm.VisibleItems);
        Assert.True(vm.ShowNoMatches);

        vm.Search = "";
        Assert.False(vm.ShowNoMatches);
    }

    [AvaloniaFact]
    public void Category_chips_narrow_the_list()
    {
        var vm = NewVm();

        vm.SelectCategoryCommand.Execute("Weapons");

        Assert.Equal("Weapons", vm.SelectedCategory);
        Assert.All(vm.VisibleItems, i => Assert.Equal("Weapons", i.Category));

        vm.SelectCategoryCommand.Execute("All");
        Assert.True(vm.VisibleItems.Count > 1);
    }

    [AvaloniaFact]
    public void An_empty_catalogue_shows_the_empty_state()
    {
        var vm = new ItemsViewModel(items: Array.Empty<ItemSummary>());

        Assert.False(vm.HasItems);
        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowNoMatches);
    }

    [AvaloniaFact]
    public void The_filters_combine()
    {
        var vm = NewVm();

        vm.SelectCategoryCommand.Execute("Weapons");
        vm.Search = "beans";           // matches Canned Beans (Food), not any Weapon

        Assert.Empty(vm.VisibleItems);
        Assert.True(vm.ShowNoMatches);

        vm.Search = "";
        Assert.NotEmpty(vm.VisibleItems);
        Assert.All(vm.VisibleItems, i => Assert.Equal("Weapons", i.Category));
    }

    private static ItemsViewModel ManyCategories()
    {
        var items = Enumerable.Range(1, 20)
            .Select(i => new ItemSummary($"Item {i:D2}", $"Cat{i:D2}", "", editable: true))
            .ToList();
        return new ItemsViewModel(items: items);
    }

    [AvaloniaFact]
    public void Only_a_few_category_chips_show_until_more_is_clicked()
    {
        var vm = ManyCategories();

        Assert.True(vm.HasMoreCategories);
        Assert.Equal(ItemsViewModel.CategoryPreviewCount, vm.ShownCategories.Count);
        Assert.Equal("More…", vm.CategoriesToggleLabel);

        vm.ToggleCategoriesCommand.Execute(null);

        Assert.Equal(vm.Categories.Count, vm.ShownCategories.Count);
        Assert.Equal("Less", vm.CategoriesToggleLabel);
    }

    [AvaloniaFact]
    public void The_selected_category_stays_visible_when_collapsed()
    {
        var vm = ManyCategories();

        vm.SelectCategoryCommand.Execute("Cat20");   // well past the preview

        Assert.Contains("Cat20", vm.ShownCategories);
    }
}
