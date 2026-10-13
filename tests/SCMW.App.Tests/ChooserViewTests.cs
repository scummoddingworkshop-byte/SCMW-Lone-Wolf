using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

/// <summary>
/// The friendly chooser: "What would you like to change?" Each tile opens a kind of content.
/// Until Phase 2's Workspace exists, that means the kind's own screen.
/// </summary>
public class ChooserViewTests
{
    private static ChooserView Shown(INavigation nav)
    {
        var view = new ChooserView { DataContext = new ChooserViewModel(nav) };
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
    }

    [AvaloniaFact]
    public void A_kind_tile_opens_that_kind()
    {
        var nav = new NavigationHost();
        var view = Shown(nav);

        var tile = view.FindControl<Button>("ItemsTile")!;
        Assert.NotNull(tile.Command);
        tile.Command!.Execute(null);

        Assert.Equal(ScreenKey.Items, nav.Current);
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
    public void A_chooser_tile_in_home_opens_that_kind()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav);
        var window = new MainWindow { DataContext = vm };
        window.Show();

        var tile = window.GetVisualDescendants().OfType<Button>()
                         .Single(b => b.Name == "RecipesTile");
        Assert.NotNull(tile.Command);
        tile.Command!.Execute(null);

        Assert.Equal(ScreenKey.Recipes, nav.Current);
    }
}
