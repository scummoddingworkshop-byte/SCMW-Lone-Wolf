using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
        Assert.Contains("Your projects", texts);
        Assert.Contains("Build your mod", texts);
        Assert.Contains("Manage your mods", texts);
        Assert.Contains("Backups", texts);
    }

    [AvaloniaFact]
    public void The_hero_is_a_slim_band_that_follows_its_width()
    {
        var view = ShownView();

        var hero = view.FindControl<Border>("Hero")!;
        Assert.True(hero.Bounds.Height > 100, "the hero should size itself from its width");

        // About a quarter of its width: a slim band, so the page's own content stays above the fold.
        var ratio = hero.Bounds.Height / hero.Bounds.Width;
        Assert.InRange(ratio, 0.20, 0.28);
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
    public void Home_project_tiles_are_all_the_same_size()
    {
        var view = ShownView();

        // The same card the Projects screen uses, so a project looks the same in both places.
        var tiles = view.GetVisualDescendants().OfType<ProjectTile>().ToList();

        Assert.Equal(4, tiles.Count);
        var widths = tiles.Select(t => Math.Round(t.Bounds.Width)).Distinct().ToList();
        var heights = tiles.Select(t => Math.Round(t.Bounds.Height)).Distinct().ToList();
        Assert.True(widths.Count == 1, $"tile widths differ: {string.Join(", ", widths)}");
        Assert.True(heights.Count == 1, $"tile heights differ: {string.Join(", ", heights)}");
    }

    [AvaloniaFact]
    public void Home_project_open_buttons_line_up_across_the_cards()
    {
        var view = ShownView();

        var opens = view.GetVisualDescendants().OfType<ProjectTile>()
            .Select(t => t.FindControl<Button>("OpenButton")!)
            .ToList();

        Assert.Equal(4, opens.Count);

        // The first row's cards: every Open button sits on the same line, whatever the name or
        // description above it. (The fourth project is on the next row, so it sits lower.)
        var tops = opens.Take(3)
            .Select(b => Math.Round(b.TranslatePoint(new Point(0, 0), view)!.Value.Y))
            .Distinct()
            .ToList();

        Assert.True(tops.Count == 1, $"the Open buttons sit at different heights: {string.Join(", ", tops)}");

        var fourth = Math.Round(opens[3].TranslatePoint(new Point(0, 0), view)!.Value.Y);
        Assert.True(fourth > tops[0], "the fourth card should start on the next row");
    }

    [AvaloniaFact]
    public void A_project_description_is_shown_in_full()
    {
        var view = ShownView();

        // No line cap and no ellipsis: a description reads whole. The cards stay equal height
        // because the grid's cells are uniform, so this costs nothing.
        var described = view.GetVisualDescendants().OfType<ProjectTile>()
            .Select(t => t.FindControl<TextBlock>("DescriptionText")!)
            .Where(t => !string.IsNullOrEmpty(t.Text))
            .ToList();

        Assert.NotEmpty(described);
        Assert.All(described, t => Assert.Equal(0, t.MaxLines));
        Assert.All(described, t => Assert.Equal(TextTrimming.None, t.TextTrimming));
    }

    [Fact]
    public void Three_project_cards_a_row_normally_and_more_only_when_really_wide()
    {
        var convert = new ColumnCountConverter();

        Assert.Equal(3, convert.Convert(900d, typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Equal(3, convert.Convert(380d * 3, typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Equal(4, convert.Convert(380d * 4, typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Equal(6, convert.Convert(380d * 40, typeof(int), null, CultureInfo.InvariantCulture));
    }
}
