using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace SCMW.App.Tests;

public class ShellContentTests
{
    [AvaloniaFact]
    public void The_shell_owns_one_content_source_and_gives_it_to_items()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            // Default settings have no game path => a NotConfigured source, sample items.
            var vm = new ShellViewModel(new NavigationHost(), projects: projects);

            Assert.NotNull(vm.ContentSource);
            Assert.False(vm.Items.IsUsingRealItems);
            Assert.Contains(vm.Items.VisibleItems, i => i.Name == "Duct Tape");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public async Task Loading_from_a_shell_source_never_throws_without_a_game()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: projects);

            await vm.Items.LoadFromSourceAsync();     // no game configured: keeps the samples

            Assert.Contains(vm.Items.VisibleItems, i => i.Name == "Duct Tape");
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void The_sample_note_is_actually_visible_on_the_items_screen()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: projects);
            vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Items);

            var window = new MainWindow { DataContext = vm };
            window.Show();

            var note = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text is not null && t.Text.Contains("sample", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(note);
            Assert.True(note!.IsEffectivelyVisible, "the sample note is present but not visible");
        }
        finally { TestProjects.Cleanup(root); }
    }
}
