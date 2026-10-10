using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SCMW.Core.Settings;

namespace SCMW.App.Tests;

/// <summary>
/// Renders the app screens at 1920x1080 into <c>artifacts/media</c>, for store/listing images
/// (Nexus Mods, itch.io, etc.). Not assertions about behaviour — they just produce the pictures.
/// </summary>
public class NexusMediaTests : IDisposable
{
    private readonly List<string> _roots = new();
    public void Dispose() { foreach (var root in _roots) TestProjects.Cleanup(root); }

    private ShellViewModel Shell(params string[] projectNames)
    {
        var (store, root) = TestProjects.With(projectNames.Length > 0 ? projectNames : new[] { "Duct Tape Expanded" });
        _roots.Add(root);
        return new ShellViewModel(new NavigationHost(), projects: store);
    }

    /// <summary>A shell that looks "set up" (a game path + key) with a ready source of real item names.</summary>
    private ShellViewModel ConfiguredShell(params string[] projectNames)
    {
        var (store, root) = TestProjects.With(projectNames.Length > 0 ? projectNames : new[] { "Duct Tape Expanded" });
        _roots.Add(root);

        var settings = new InMemorySettingsStore();
        settings.Save(new AppSettings(
            WorkspacePath: null,
            GamePath: @"C:\Program Files (x86)\Steam\steamapps\common\SCUM",
            BuildId: null, AesKey: "demo-key", ActiveProjectId: null));

        return new ShellViewModel(new NavigationHost(), settingsStore: settings, projects: store,
            contentSource: new ScreenshotTests.ReadyItemsSource());
    }

    private static void Save(Window window, string name)
    {
        window.Width = 1920;
        window.Height = 1080;
        window.Show();

        var path = Screenshot.Capture(window, name, "media");
        Assert.True(new FileInfo(path).Length > 5_000, path);
    }

    [AvaloniaFact]
    public void Media_home()
    {
        var vm = ConfiguredShell("Duct Tape Expanded", "Military Backpack+", "Ammo Stack Adjustments");
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Home);
        Save(new MainWindow { DataContext = vm }, "nexus-home");
    }

    [AvaloniaFact]
    public void Media_projects()
    {
        var vm = Shell("Duct Tape Expanded", "Military Backpack+", "Ammo Stack Adjustments");
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Projects);
        Save(new MainWindow { DataContext = vm }, "nexus-projects");
    }

    [AvaloniaFact]
    public void Media_items()
    {
        var vm = ConfiguredShell("Inventory Rebalance");
        vm.Items.LoadFromSourceAsync().GetAwaiter().GetResult();
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Items);

        vm.Items.SelectedItem = vm.Items.VisibleItems.First(i => i.Name == "Improvised Bow");
        vm.Items.ShowAllValues = true;
        vm.Items.LoadFieldsAsync(vm.Items.SelectedItem).GetAwaiter().GetResult();

        Save(new MainWindow { DataContext = vm }, "nexus-items");
    }

    [AvaloniaFact]
    public void Media_build()
    {
        var vm = Shell("Duct Tape Expanded");
        vm.ItemChanges.RecordEdit("Duct Tape", "Max stack", "10", "25", vm.ProjectStore.Current!.Id);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Build);
        Save(new MainWindow { DataContext = vm }, "nexus-build");
    }

    [AvaloniaFact]
    public void Media_mods()
    {
        var vm = Shell("Duct Tape Expanded");
        vm.ModList.Install("SCMW_Duct-Tape-Expanded_v1.0.0_P");
        vm.ModList.Add("SomeExternalMod.pak");
        var external = vm.ModList.Add("VehiclePack.pak");
        external.IsOn = false;
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);
        Save(new MainWindow { DataContext = vm }, "nexus-mods");
    }

    [AvaloniaFact]
    public void Media_settings()
    {
        var vm = Shell("Duct Tape Expanded");
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);
        Save(new MainWindow { DataContext = vm }, "nexus-settings");
    }
}
