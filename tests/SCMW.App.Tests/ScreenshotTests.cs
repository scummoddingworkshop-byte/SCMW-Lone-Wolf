using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Views;
using SCMW.Backend.Build;

namespace SCMW.App.Tests;

public class ScreenshotTests
{
    [AvaloniaFact]
    public void Window_can_be_captured_to_a_png()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var path = Screenshot.Capture(window, "shell-before");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_shell()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Show();

        var path = Screenshot.Capture(window, "shell-v2");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_home()
    {
        var window = new MainWindow { DataContext = new ShellViewModel(new NavigationHost()) };
        window.Width = 1200;
        window.Height = 1080;
        window.Show();

        var path = Screenshot.Capture(window, "home");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_home_empty()
    {
        var home = new HomeView
        {
            DataContext = new HomeViewModel(new NavigationHost(), Array.Empty<ProjectSummary>()),
        };
        var window = new Window { Content = home, Width = 1100, Height = 800 };
        window.Show();

        var path = Screenshot.Capture(window, "home-empty");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_projects()
    {
        var nav = new NavigationHost();
        var vm = new ShellViewModel(nav, projects: RealProjects(3));
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Projects);

        var window = new MainWindow { DataContext = vm };
        window.Width = 1200;
        window.Height = 1080;
        window.Show();

        var path = Screenshot.Capture(window, "projects");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    /// <summary>A throwaway workspace holding <paramref name="count"/> real projects.</summary>
    private static ProjectStore RealProjects(int count)
    {
        // A deterministic folder per call, cleared first — so runs never litter temp.
        var dir = Path.Combine(Path.GetTempPath(), "SCMW-shots", $"p{count}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        var store = new ProjectStore(
            new SCMW.Core.Projects.ProjectService(SCMW.Core.Workspace.WorkspaceService.Ensure(dir)),
            new InMemorySettingsStore());

        var names = new[] { "Duct Tape Expanded", "Military Backpack+", "Ammo Stack Adjustments", "Vehicle Tweaks" };
        var descriptions = new[]
        {
            "Larger inventory size and increased stack amount",
            "Increased storage capacity and balanced weight",
            "Higher stack sizes for common ammunition types",
            "Adjusted storage and fuel capacity",
        };

        for (var i = 0; i < count; i++) store.Create(names[i % names.Length], descriptions[i % descriptions.Length]);
        if (count > 0) store.SetCurrent(store.All()[0].Id);
        return store;
    }

    [AvaloniaFact]
    public void Capture_projects_empty()
    {
        var view = new ProjectsView
        {
            DataContext = new ProjectsViewModel(RealProjects(0)),
        };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();

        var path = Screenshot.Capture(window, "projects-empty");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_projects_create_panel()
    {
        var vm = new ProjectsViewModel(RealProjects(2));
        vm.NewProjectCommand.Execute(null);
        var view = new ProjectsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();

        var path = Screenshot.Capture(window, "projects-create");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_items()
    {
        var vm = new ShellViewModel(new NavigationHost(), projects: RealProjects(1));
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Items);

        // Open an item and change a value, so the picture shows both states.
        var item = vm.Items.VisibleItems.First(i => i.Name == "Duct Tape");
        vm.Items.SelectedItem = item;
        var stack = item.Values.First(v => v.Name == "Max stack");
        vm.Items.BeginEditCommand.Execute(stack);
        stack.Draft = "25";
        vm.Items.CommitEditCommand.Execute(stack);

        var window = new MainWindow { DataContext = vm };
        window.Width = 1280;
        window.Height = 900;
        window.Show();

        var path = Screenshot.Capture(window, "items");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_items_real()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var source = new ReadyItemsSource();
            var vm = new ShellViewModel(new NavigationHost(), projects: projects, contentSource: source);
            vm.Items.LoadFromSourceAsync().GetAwaiter().GetResult();
            vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Items);
            vm.Items.SelectedItem = vm.Items.VisibleItems.First(i => i.Name == "Improvised Bow");
            vm.Items.ShowAllValues = true;          // the real fields are read-only; show them
            vm.Items.LoadFieldsAsync(vm.Items.SelectedItem).GetAwaiter().GetResult();

            var window = new MainWindow { DataContext = vm };
            window.Width = 1280;
            window.Height = 900;
            window.Show();

            var path = Screenshot.Capture(window, "items-real");

            Assert.True(File.Exists(path), path);
            Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
        }
        finally { TestProjects.Cleanup(root); }
    }

    /// <summary>A frozen, ready source with real SCUM item names — for pictures only.</summary>
    internal sealed class ReadyItemsSource : SCMW.Core.Content.IContentSource
    {
        private sealed class NoIcons : SCMW.Core.Content.IIconCache
        {
            public string IconsDirectory => "";
            public string? GetIconPath(string assetId) => null;
            public int Count => 0;
            public long TotalBytes => 0;
            public void Clear() { }
        }

        private static readonly (string Id, string Name, string Cat)[] Items =
        {
            ("SCUM/Content/ConZ_Files/Items/Weapons/Ranged_Weapons/Improvised_Bow_ES", "Improvised Bow", "Weapons"),
            ("SCUM/Content/ConZ_Files/Items/Food/Canned_Food/CannedSpaghetti_ES", "Canned Spaghetti", "Food"),
            ("SCUM/Content/ConZ_Files/Items/Clothes/Gloves/RH_Arm_Guard", "RH Arm Guard", "Clothes"),
            ("SCUM/Content/ConZ_Files/Items/Equipment/Barrel", "Barrel", "Equipment"),
            ("SCUM/Content/ConZ_Files/Items/Drink/Tequila", "Tequila", "Drink"),
            ("SCUM/Content/ConZ_Files/Items/Medications/Adrenaline_Shot", "Adrenaline Shot", "Medications"),
        };

        public SCMW.Core.Content.ContentSourceStatus Status =>
            new(SCMW.Core.Content.ContentSourceState.Ready, "mounted", "25247492");
        public SCMW.Core.Content.ScanInfo? LastScan => null;
        public SCMW.Core.Content.IIconCache Icons { get; } = new NoIcons();
        public IReadOnlyList<string> Categories => Items.Select(i => i.Cat).Distinct().ToList();
        public void Configure(string p, string? k, string? b) { }
        public Task<SCMW.Core.Content.ContentSourceStatus> ScanAsync(
            IProgress<int>? p = null, CancellationToken ct = default) => Task.FromResult(Status);
        public Task<IReadOnlyList<SCMW.Core.Content.AssetSummary>> SearchAsync(
            string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SCMW.Core.Content.AssetSummary>>(
                Items.Select(i => new SCMW.Core.Content.AssetSummary(
                    i.Id, i.Name, i.Cat, SCMW.Core.StatusKind.Unknown, 0, i.Id + ".uasset", null)).ToList());
        public Task<IReadOnlyList<SCMW.Core.Content.AssetField>> FieldsAsync(
            string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SCMW.Core.Content.AssetField>>(new[]
            {
                new SCMW.Core.Content.AssetField("Caption", "Caption", SCMW.Core.StatusKind.ReadOnly, "Fletched Wooden Arrow", "TextProperty"),
                new SCMW.Core.Content.AssetField("Description", "Description", SCMW.Core.StatusKind.ReadOnly,
                    "An arrow crafted from a stick. It is enhanced with feather fletchings. It can be used to hunt small game and it flies quite straight.", "TextProperty"),
                new SCMW.Core.Content.AssetField("MaxStackSize", "MaxStackSize", SCMW.Core.StatusKind.ReadOnly, "10"),
                new SCMW.Core.Content.AssetField("GridInventoryRowSpan", "GridInventoryRowSpan", SCMW.Core.StatusKind.ReadOnly, "2"),
                new SCMW.Core.Content.AssetField("Weight", "Weight", SCMW.Core.StatusKind.ReadOnly, "1.5"),
                new SCMW.Core.Content.AssetField("_itemTags", "_itemTags", SCMW.Core.StatusKind.ReadOnly, "(tags)", "ArrayProperty"),
            });
        public Task<SCMW.Core.Content.IconExtractionProgress> ExtractAllIconsAsync(
            IProgress<SCMW.Core.Content.IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new SCMW.Core.Content.IconExtractionProgress(0, 0, 0, 0));
        public Task<SCMW.Core.Content.IconExtractionProgress> ExtractIconsAsync(
            IEnumerable<string> ids, IProgress<SCMW.Core.Content.IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new SCMW.Core.Content.IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Capture_items_machinery()
    {
        var vm = new ShellViewModel(new NavigationHost(), projects: RealProjects(1));

        // A change saved in the current project, then loaded back — the machinery picture.
        var current = vm.ProjectStore.Current!;
        vm.ItemChanges.RecordEdit("Duct Tape", "Max stack", "10", "25", current.Id);

        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Items);
        vm.Items.SelectedItem = vm.Items.VisibleItems.First(i => i.Name == "Duct Tape");

        var window = new MainWindow { DataContext = vm };
        window.Width = 1280;
        window.Height = 900;
        window.Show();

        var path = Screenshot.Capture(window, "items-machinery");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_items_none_selected()
    {
        var view = new ItemsView { DataContext = new ItemsViewModel() };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();

        var path = Screenshot.Capture(window, "items-none-selected");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_items_no_matches()
    {
        var vm = new ItemsViewModel { Search = "zzz-no-match" };
        var view = new ItemsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();

        var path = Screenshot.Capture(window, "items-no-matches");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_build()
    {
        var vm = new ShellViewModel(new NavigationHost(), projects: RealProjects(1));
        var current = vm.ProjectStore.Current!;
        vm.ItemChanges.RecordEdit("Duct Tape", "Max stack", "10", "25", current.Id);
        vm.ItemChanges.RecordEdit("AK-15", "Damage", "45", "60", current.Id);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Build);

        var window = new MainWindow { DataContext = vm };
        window.Width = 1200;
        window.Height = 900;
        window.Show();

        var path = Screenshot.Capture(window, "build");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_build_running()
    {
        var (_, vm, window) = BuildWindow();
        vm.BuildCommand.Execute(null);
        vm.Advance();
        vm.Advance();

        var path = Screenshot.Capture(window, "build-running");
        Assert.True(File.Exists(path), path);
    }

    [AvaloniaFact]
    public void Capture_build_success()
    {
        var (_, vm, window) = BuildWindow();
        vm.BuildCommand.Execute(null);
        for (var i = 0; i < 5; i++) vm.Advance();

        var path = Screenshot.Capture(window, "build-success");
        Assert.True(File.Exists(path), path);
    }

    [AvaloniaFact]
    public void Capture_build_machinery()
    {
        var vm = new ShellViewModel(new NavigationHost(), projects: RealProjects(1));
        var current = vm.ProjectStore.Current!;
        vm.ItemChanges.RecordEdit("Duct Tape", "Max stack", "10", "25", current.Id);
        vm.ItemChanges.RecordEdit("AK-15", "Damage", "45", "60", current.Id);
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Build);

        var window = new MainWindow { DataContext = vm };
        window.Width = 1200;
        window.Height = 900;
        window.Show();

        var path = Screenshot.Capture(window, "build-machinery");
        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_build_empty()
    {
        var view = new BuildView { DataContext = new BuildViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();

        var path = Screenshot.Capture(window, "build-empty");
        Assert.True(File.Exists(path), path);
    }

    [AvaloniaFact]
    public void Capture_build_failure()
    {
        var (_, vm, window) = BuildWindow();
        vm.ShowFailure(
            "The mod could not be made.",
            "Something in the game files was not in the shape we expected.",
            "Check your SCUM folder in Settings, then try again.",
            "System.IO.IOException: unexpected end of stream");

        var path = Screenshot.Capture(window, "build-failure");
        Assert.True(File.Exists(path), path);
    }

    [AvaloniaFact]
    public void Capture_build_permission_failure()
    {
        var vm = Buildable(elevation: new ScreenshotElevation());
        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();

        vm.ShowFailure(
            "The mod could not be installed.",
            "Windows blocked SCMW from writing to your SCUM folder, so it couldn't install the mod into " +
            "SCUM. This usually means SCUM is installed in a protected place like Program Files. " +
            "Restart SCMW as administrator, then try again.",
            "", "", needsAdmin: true);

        var path = Screenshot.Capture(window, "build-permission");
        Assert.True(File.Exists(path), path);
    }

    [AvaloniaFact]
    public void Capture_mods()
    {
        var vm = new ShellViewModel(new NavigationHost());
        vm.ModList.Install("My first mod");
        var external = vm.ModList.Add("SomeMod.pak");
        vm.ModList.Add("VehiclePack.pak");
        external.IsOn = false;                 // one switched off
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Mods);

        var window = new MainWindow { DataContext = vm };
        window.Width = 1100;
        window.Height = 800;
        window.Show();

        var path = Screenshot.Capture(window, "mods");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_mods_empty()
    {
        var view = new ModsView { DataContext = new ModsViewModel(new ModList()) };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();

        var path = Screenshot.Capture(window, "mods-empty");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_mods_confirm()
    {
        var store = new ModList();
        var mod = store.Add("SomeMod.pak");
        mod.IsConfirmingRemove = true;
        var view = new ModsView { DataContext = new ModsViewModel(store) };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();

        var path = Screenshot.Capture(window, "mods-confirm");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_settings()
    {
        var vm = new ShellViewModel(new NavigationHost());
        vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);

        var window = new MainWindow { DataContext = vm };
        window.Width = 1200;
        window.Height = 1100;
        window.Show();

        var path = Screenshot.Capture(window, "settings");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_settings_support()
    {
        var view = new SettingsView { DataContext = new SettingsViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();

        view.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var path = Screenshot.Capture(window, "settings-support");
        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_settings_troubleshooting()
    {
        var view = new SettingsView { DataContext = new SettingsViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 1100 };
        window.Show();

        // Open the logs/troubleshooting section (it holds the licence-files button now).
        view.GetVisualDescendants().OfType<Expander>()
            .First(e => e.Name == "Support")
            .IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var path = Screenshot.Capture(window, "settings-troubleshooting");

        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public void Capture_settings_real()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-shot-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A real settings file (in a temp folder), so the picture shows the app as it is.
            var store = new SCMW.Core.Settings.JsonSettingsStore(dir);
            store.Save(new SCMW.Core.Settings.AppSettings(
                WorkspacePath: @"C:\Users\Fran\Documents\SCMW", GamePath: null));

            var vm = new ShellViewModel(new NavigationHost(), store);
            vm.SelectedEntry = vm.Sidebar.Single(i => i.Key == ScreenKey.Settings);

            var window = new MainWindow { DataContext = vm };
            window.Width = 1200;
            window.Height = 1100;
            window.Show();

            var path = Screenshot.Capture(window, "settings-real");
            Assert.True(File.Exists(path), path);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static (BuildView view, BuildViewModel vm, Window window) BuildWindow()
    {
        var vm = Buildable();
        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        return (view, vm, window);
    }

    /// <summary>A Build view model over a throwaway project holding one saved change.</summary>
    private static BuildViewModel Buildable(IBuildService? service = null, IElevation? elevation = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SCMW-shots", "build");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        var projects = new ProjectStore(
            new SCMW.Core.Projects.ProjectService(SCMW.Core.Workspace.WorkspaceService.Ensure(dir)),
            new InMemorySettingsStore());
        projects.Create("My first mod", "");
        projects.SetCurrent(projects.All()[0].Id);

        var changes = new ItemChangeStore(new SCMW.Core.Editing.ProjectChangeStore(), projects);
        changes.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);
        return new BuildViewModel(changes, projects: projects, service: service, elevation: elevation);
    }

    [AvaloniaFact]
    public async Task Capture_build_pak()
    {
        var vm = Buildable(new AlwaysOkScreenshotService());
        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        await vm.BuildCommand.ExecuteAsync(null);

        var path = Screenshot.Capture(window, "build-pak");
        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    [AvaloniaFact]
    public async Task Capture_build_installed()
    {
        var vm = Buildable(new AlwaysOkScreenshotService());
        var view = new BuildView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        await vm.BuildCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        var path = Screenshot.Capture(window, "build-installed");
        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    /// <summary>A stand-in build service for the picture only — no game, no files.</summary>
    private sealed class ScreenshotElevation : IElevation
    {
        public bool IsElevated => false;
        public bool RestartAsAdmin() => true;
    }

    private sealed class AlwaysOkScreenshotService : IBuildService
    {
        private bool _installed;
        public Task<BuildOutcome> BuildAsync(string n, string v, IReadOnlyList<BuildItem> i, IProgress<int>? p, CancellationToken ct)
        { p?.Report(4); return Task.FromResult(new BuildOutcome(true, @"C:\Users\Fran\Documents\SCMW\Builds\My-first-mod\SCMW_My-first-mod_v1.0.0_P.pak", null)); }
        public Task<InstallOutcome> InstallAsync(string id, string n, string v, string pak, CancellationToken ct)
        { _installed = true; return Task.FromResult(new InstallOutcome(true, @"C:\Games\SCUM\SCUM\Content\Paks\~mods\SCMW_My-first-mod_v1.0.0_P.pak", null)); }
        public Task<RemoveOutcome> RemoveAsync(string id, CancellationToken ct)
        { _installed = false; return Task.FromResult(new RemoveOutcome(true, null)); }
        public bool IsInstalled(string id) => _installed;
    }

    [AvaloniaFact]
    public void Capture_mods_real()
    {
        var svc = new ScreenshotModsService();
        svc.Entries.Add(new ModsEntry("SCMW_My-first-mod_v1.0.0_P.pak", "SCMW My first mod", true, false));
        svc.Entries.Add(new ModsEntry("Downloaded_Gun.pak", "Downloaded Gun", true, true));
        svc.Entries.Add(new ModsEntry("Old_Tweaks.pak.disabled", "Old Tweaks", false, true));

        var vm = new ModsViewModel(service: svc);
        var view = new ModsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        window.Show();

        var path = Screenshot.Capture(window, "mods-real");
        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length > 5_000, "PNG looks empty");
    }

    /// <summary>A stand-in mods service for the picture only — no game, no disk.</summary>
    private sealed class ScreenshotModsService : IModsService
    {
        public readonly List<ModsEntry> Entries = new();
        public bool IsAvailable => true;
        public IReadOnlyList<ModsEntry> List() => Entries.ToList();
        public ModsResult SetEnabled(string file, bool enabled) => new(true, null);
        public ModsResult SetAllEnabled(bool enabled) => new(true, null);
        public ModsResult Remove(string file) => new(true, null);
        public ImportOutcome Import(IReadOnlyList<string> paths) => new(true, null, 0, 0);
    }
}

