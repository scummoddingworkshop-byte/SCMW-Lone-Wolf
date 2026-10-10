using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class RealProjectsWiringTests
{
    [AvaloniaFact]
    public void The_header_home_and_build_all_follow_the_current_project()
    {
        var (store, root) = TestProjects.With("My first mod");
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: store);

            Assert.Equal("My first mod", vm.ActiveProjectText);
            Assert.Contains(vm.Home.RecentProjects, p => p.Name == "My first mod");
            Assert.Equal("My first mod", vm.Build.ProjectName);

            // Rename it — everything follows.
            store.Rename(store.Current!.Id, "Tape mod");

            Assert.Equal("Tape mod", vm.ActiveProjectText);
            Assert.Contains(vm.Home.RecentProjects, p => p.Name == "Tape mod");
            Assert.Equal("Tape mod", vm.Build.ProjectName);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void With_no_projects_everything_says_so_plainly()
    {
        var (store, root) = TestProjects.With();
        try
        {
            var vm = new ShellViewModel(new NavigationHost(), projects: store);

            Assert.Equal("No project open", vm.ActiveProjectText);
            Assert.Equal("No project open", vm.Build.ProjectName);
            Assert.False(vm.Home.HasProjects);
            Assert.True(vm.Projects.ShowEmptyState);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_project_shows_its_picture_when_it_has_one_and_the_default_when_it_does_not()
    {
        var (store, root) = TestProjects.With("With picture", "Without picture");
        try
        {
            // A real (tiny) PNG, exactly what the engine would copy in.
            var png = Path.Combine(root, "icon.png");
            File.WriteAllBytes(png, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

            var withPicture = store.All().Single(p => p.Name == "With picture");
            store.SetIcon(withPicture.Id, png);

            var shown = ProjectSummary.From(store.All().Single(p => p.Name == "With picture"), false);
            var plain = ProjectSummary.From(store.All().Single(p => p.Name == "Without picture"), false);

            Assert.NotNull(shown.Picture);
            Assert.Null(plain.Picture);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void When_times_read_plainly()
    {
        Assert.Equal("just now", ProjectSummary.Describe(DateTime.UtcNow));
        Assert.Equal("2 hours ago", ProjectSummary.Describe(DateTime.UtcNow.AddHours(-2)));
        Assert.Equal("1 day ago", ProjectSummary.Describe(DateTime.UtcNow.AddDays(-1)));
        Assert.Equal("3 days ago", ProjectSummary.Describe(DateTime.UtcNow.AddDays(-3)));
    }
}
