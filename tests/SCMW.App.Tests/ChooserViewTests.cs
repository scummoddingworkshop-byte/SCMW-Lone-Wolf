using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

/// <summary>
/// The friendly chooser: "What would you like to change?" Each tile opens the Workspace with
/// that kind of content already picked.
/// </summary>
public class ChooserViewTests
{
    private static ChooserView Shown(INavigation nav, WorkspaceViewModel? workspace = null)
    {
        var view = new ChooserView { DataContext = new ChooserViewModel(nav, workspace) };
        var window = new Window { Content = view, Width = 900, Height = 400 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void The_chooser_asks_what_you_would_like_to_change()
    {
        var view = Shown(new NavigationHost());

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("What would you like to change?", texts);
        Assert.Contains("Items", texts);
        Assert.Contains("Base building", texts);
        Assert.Contains("Recipes", texts);
        Assert.Contains("New recipes & objects", texts);
    }

    [AvaloniaFact]
    public void A_kind_tile_opens_the_workspace_with_that_kind()
    {
        var nav = new NavigationHost();
        var workspace = new WorkspaceViewModel();
        var view = Shown(nav, workspace);

        var tile = view.FindControl<Button>("ItemsTile")!;
        Assert.NotNull(tile.Command);
        tile.Command!.Execute(null);

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal("Items", workspace.SelectedKind);
    }

    [AvaloniaFact]
    public void Home_shows_the_chooser()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("What would you like to change?", texts);
    }

    [AvaloniaFact]
    public void A_chooser_tile_in_home_opens_the_workspace_with_that_kind()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm };
        window.Show();

        var tile = window.GetVisualDescendants().OfType<Button>()
                         .Single(b => b.Name == "RecipesTile");
        Assert.NotNull(tile.Command);
        tile.Command!.Execute(null);

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal("Recipes", vm.Workspace.SelectedKind);
    }

    [AvaloniaFact]
    public void The_new_content_tile_opens_the_workspace_and_starts_a_new_recipe()
    {
        var nav = new NavigationHost();
        var workspace = new WorkspaceViewModel();
        var view = Shown(nav, workspace);

        view.FindControl<Button>("NewContentTile")!.Command!.Execute(null);

        Assert.Equal(ScreenKey.Workspace, nav.Current);
        Assert.Equal("Recipes", workspace.SelectedKind);   // the new-recipe panel lives in Recipes
        Assert.True(workspace.ShowNewRecipe);
    }
}
