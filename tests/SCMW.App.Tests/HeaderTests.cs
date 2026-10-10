using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class HeaderTests
{
    [AvaloniaFact]
    public void Window_has_a_header_above_the_rail_and_content()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var header = window.FindControl<HeaderBar>("Header");
        Assert.NotNull(header);

        var shown = header!.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsVisible).Select(t => t.Text).ToList();

        Assert.Contains("No project open", shown);                // nothing open yet
        Assert.DoesNotContain($"v{AppInfo.Version}", shown);      // the brand lives in the title bar now
        Assert.Empty(header.GetVisualDescendants().OfType<IconButton>());
    }

    [AvaloniaFact]
    public void The_header_shows_the_open_project()
    {
        var (store, root) = TestProjects.With("Duct Tape Expanded");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: store);
            var window = new MainWindow { DataContext = vm };
            window.Show();

            var header = window.FindControl<HeaderBar>("Header")!;
            var shown = header.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsVisible).Select(t => t.Text).ToList();

            Assert.Contains("Duct Tape Expanded", shown);      // the open project is named
            Assert.DoesNotContain("No project open", shown);   // and the empty line is hidden
        }
        finally { TestProjects.Cleanup(root); }
    }
}
