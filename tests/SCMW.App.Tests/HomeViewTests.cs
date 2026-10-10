using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class HomeViewTests
{
    /// <summary>
    /// HomeView must be attached to a shown window before its visual children (and the
    /// text inside buttons) exist, so every test hosts it the same way.
    /// </summary>
    private static HomeView ShownView(IReadOnlyList<ProjectSummary>? projects = null)
    {
        var view = new HomeView { DataContext = new HomeViewModel(new NavigationHost(), projects) };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void Home_shows_the_hero_and_the_four_quick_actions()
    {
        var view = ShownView();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.NotNull(view.FindControl<Border>("Hero"));        // the banner artwork
        Assert.Contains("Start a new project", texts);
        Assert.Contains("Open a project", texts);
        Assert.Contains("Browse items", texts);
        Assert.Contains("Build & play", texts);
    }

    [AvaloniaFact]
    public void The_hero_height_follows_its_width()
    {
        var view = ShownView();

        var hero = view.FindControl<Border>("Hero")!;
        Assert.True(hero.Bounds.Height > 100, "the hero should size itself from its width");
    }

    [AvaloniaFact]
    public void The_scale_converter_multiplies_by_its_parameter()
    {
        var converter = new ScaleConverter();

        Assert.Equal(360d, (double?)converter.Convert(1000d, typeof(double), "0.36", CultureInfo.InvariantCulture));
        Assert.Equal(1000d, (double?)converter.Convert(1000d, typeof(double), null, CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void The_quick_actions_each_have_an_icon()
    {
        var view = ShownView();

        var quick = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("quickaction")).ToList();

        Assert.Equal(4, quick.Count);
        foreach (var button in quick)
        {
            var image = button.GetVisualDescendants().OfType<Image>().FirstOrDefault();
            Assert.NotNull(image);
            Assert.NotNull(image!.Source);        // the icon actually loaded
        }
    }

    [AvaloniaFact]
    public void Home_shows_recent_projects()
    {
        var view = ShownView();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("RECENT PROJECTS", texts);
        Assert.Contains("Duct Tape Expanded", texts);
    }

    [AvaloniaFact]
    public void Home_shows_the_empty_state_only_when_there_are_no_projects()
    {
        var withProjects = ShownView();
        Assert.False(withProjects.FindControl<Border>("EmptyState")!.IsVisible);

        var empty = ShownView(Array.Empty<ProjectSummary>());
        Assert.True(empty.FindControl<Border>("EmptyState")!.IsVisible);
    }

    [AvaloniaFact]
    public void Home_welcome_card_hides_when_dismissed()
    {
        var view = ShownView();

        var vm = (HomeViewModel)view.DataContext!;
        Assert.True(view.FindControl<Border>("WelcomeCard")!.IsVisible);

        vm.DismissWelcomeCommand.Execute(null);

        Assert.False(view.FindControl<Border>("WelcomeCard")!.IsVisible);
    }

    [AvaloniaFact]
    public void Home_quick_actions_fill_their_columns_evenly()
    {
        var view = ShownView();

        var tiles = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("quickaction"))
            .ToList();

        Assert.Equal(4, tiles.Count);
        var widths = tiles.Select(t => Math.Round(t.Bounds.Width)).Distinct().ToList();
        Assert.True(widths.Count == 1, $"quick actions are uneven: {string.Join(", ", widths)}");
    }

    [AvaloniaFact]
    public void Home_project_cards_are_all_the_same_size()
    {
        var view = ShownView();

        var cards = view.GetVisualDescendants().OfType<ProjectCard>().ToList();

        Assert.Equal(4, cards.Count);
        var widths = cards.Select(c => Math.Round(c.Bounds.Width)).Distinct().ToList();
        var heights = cards.Select(c => Math.Round(c.Bounds.Height)).Distinct().ToList();
        Assert.True(widths.Count == 1, $"card widths differ: {string.Join(", ", widths)}");
        Assert.True(heights.Count == 1, $"card heights differ: {string.Join(", ", heights)}");

        // The visible tile is the button inside the card; it must fill the card.
        var tiles = cards.Select(c => c.FindControl<Button>("Inner")!).ToList();
        var tileHeights = tiles.Select(t => Math.Round(t.Bounds.Height)).Distinct().ToList();
        var tileWidths = tiles.Select(t => Math.Round(t.Bounds.Width)).Distinct().ToList();
        Assert.True(tileHeights.Count == 1, $"tile heights differ: {string.Join(", ", tileHeights)}");
        Assert.True(tileWidths.Count == 1, $"tile widths differ: {string.Join(", ", tileWidths)}");
    }
}
