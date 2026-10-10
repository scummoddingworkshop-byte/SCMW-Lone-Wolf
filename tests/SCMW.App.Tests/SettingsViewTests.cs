using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;

namespace SCMW.App.Tests;

public class SettingsViewTests
{
    private static SettingsView ShownView()
    {
        var view = new SettingsView { DataContext = new SettingsViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 1000 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void The_about_card_offers_the_support_link()
    {
        var view = ShownView();

        var button = view.FindControl<Button>("SupportButton");
        Assert.NotNull(button);
        Assert.True(button!.IsVisible);
        Assert.Contains("Ko-fi", (string)button.Content!, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Settings_shows_every_card()
    {
        var view = ShownView();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.NotNull(view.FindControl<Border>("GameCard"));
        Assert.NotNull(view.FindControl<Border>("KeyCard"));
        Assert.NotNull(view.FindControl<Border>("WorkspaceCard"));
        Assert.NotNull(view.FindControl<Border>("AboutCard"));
        Assert.NotNull(view.FindControl<Expander>("Support"));

        Assert.Contains("GAME", texts);
        Assert.Contains("UNLOCK KEY", texts);
        Assert.Contains("WORKSPACE", texts);
        Assert.Contains("ABOUT", texts);
    }

    [AvaloniaFact]
    public void Settings_uses_the_owner_chosen_labels()
    {
        var view = ShownView();
        var buttons = view.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();

        Assert.Contains("Detect", buttons);
        Assert.Contains("Re-read Game Data", buttons);
        Assert.DoesNotContain("Detect again", buttons);
        Assert.DoesNotContain("Rebuild game data", buttons);
    }

    [AvaloniaFact]
    public void The_notices_list_is_gone_but_the_licence_button_remains()
    {
        var view = ShownView();

        Assert.Null(view.FindControl<ItemsControl>("Notices"));   // the in-app list was dropped

        view.FindControl<Expander>("Support")!.IsExpanded = true; // the licence button lives inside it
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var buttons = view.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        Assert.Contains("Open licence files", buttons);
    }

    [AvaloniaFact]
    public void The_unlock_key_is_shown_plainly_so_it_can_be_checked()
    {
        var view = ShownView();

        var box = view.FindControl<TextBox>("KeyBox")!;

        Assert.Equal('\0', box.PasswordChar);        // not masked
    }

    [AvaloniaFact]
    public void About_shows_the_version_and_the_support_link()
    {
        var view = ShownView();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains(SCMW.App.AppInfo.Version, texts);
        Assert.NotNull(view.FindControl<Button>("SupportButton"));
    }
}
