using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.App.Views;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;
using SCMW.Core.Setup;
using SCMW.Core.Settings;

namespace SCMW.App;

/// <summary>
/// The shell's brain: the rail entries, which area is showing, the screen shown in the
/// content area, and the two launch actions. Navigation state itself lives in
/// <see cref="INavigation"/>; this view model derives its own state from it.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly INavigation _nav;
    private readonly ProjectStore _projectStore;
    private readonly HomeViewModel _home;
    private readonly HomeView _homeView;
    private readonly WorkspaceViewModel _workspaceVm;
    private readonly WorkspaceView _workspaceView;
    private readonly ProjectsViewModel _projects;
    private readonly ProjectsView _projectsView;
    private readonly ItemsViewModel _items;
    private readonly ItemsView _itemsView;
    private readonly ItemsViewModel _baseElements;
    private readonly ItemsView _baseElementsView;
    private readonly ItemsViewModel _recipes;
    private readonly ItemsView _recipesView;
    private readonly ItemChangeStore _itemChanges;
    private readonly ModList _modList;
    private readonly BuildViewModel _build;
    private readonly BuildView _buildView;
    private readonly ModsViewModel _mods;
    private readonly ModsView _modsView;
    private readonly BackupsViewModel _backups;
    private readonly BackupsView _backupsView;
    private readonly SettingsViewModel _settings;
    private readonly SettingsView _settingsView;
    private readonly HelpView _helpView;
    private IGameLauncher _launcher;
    private IModsService? _modsService;
    private IBuildService? _buildService;
    private readonly ISettingsStore _settingsStore;
    private readonly SCMW.Core.Workspace.WorkspacePaths _workspace;
    private readonly bool _launcherInjected;
    private readonly bool _modsInjected;
    public ShellViewModel(INavigation? nav = null, ISettingsStore? settingsStore = null,
                          IScumSources? scumSources = null, ProjectStore? projects = null,
                          SCMW.Core.Content.IContentSource? contentSource = null,
                          IGameLauncher? launcher = null, IModsService? modsService = null)
    {
        _nav = nav ?? NavigationHost.Shared;
        _projectStore = projects ?? ProjectStore.Default();

        var store = settingsStore ?? new InMemorySettingsStore();
        var settings = store.Load();
        var workspace = SCMW.Core.Workspace.WorkspaceService.Ensure(
            settings.WorkspacePath ?? Path.Combine(Path.GetTempPath(), "SCMW", "workspace"));
        _settingsStore = store;
        _workspace = workspace;
        ContentSource = contentSource ?? ContentSourceFactory.Create(settings, workspace);
        _buildService = BuildService.Create(settings, workspace, ContentSource);
        _launcherInjected = launcher is not null;
        _modsInjected = modsService is not null;
        _launcher = launcher ?? new ScumLauncher(settings.GamePath);
        _modsService = modsService ?? new ModsService(settings.GamePath, workspace.Root);
        CanLaunch = _launcher.IsAvailable;
        LaunchNote = CanLaunch ? "" : "Set your SCUM folder in Settings to launch.";

        _itemChanges = new ItemChangeStore(new ProjectChangeStore(), _projectStore);
        _workspaceVm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), ContentSource,
                                              new ContentCatalog(ContentSource, ContentSource.Icons),
                                              changes: _itemChanges, projects: _projectStore);
        _workspaceView = new WorkspaceView { DataContext = _workspaceVm };
        _home = new HomeViewModel(_nav, store: _projectStore,
                                  gameReady: () => IsScumConfigured(store),
                                  workspace: _workspaceVm);
        _homeView = new HomeView { DataContext = _home };
        _projects = new ProjectsViewModel(_projectStore, _nav, _itemChanges);
        _projectsView = new ProjectsView { DataContext = _projects };
        _items = new ItemsViewModel(_itemChanges, _projectStore, source: ContentSource,
                                    catalog: new ContentCatalog(ContentSource, ContentSource.Icons));
        _itemsView = new ItemsView { DataContext = _items };
        _baseElements = new ItemsViewModel(_itemChanges, _projectStore, source: ContentSource,
                                           catalog: new ContentCatalog(ContentSource, ContentSource.Icons),
                                           family: AssetFamily.BaseElements);
        _baseElementsView = new ItemsView { DataContext = _baseElements };
        _recipes = new ItemsViewModel(_itemChanges, _projectStore, source: ContentSource,
                                      catalog: new ContentCatalog(ContentSource, ContentSource.Icons),
                                      family: AssetFamily.Recipes);
        _recipesView = new ItemsView { DataContext = _recipes };
        _modList = new ModList();
        _build = new BuildViewModel(_itemChanges, _nav, TimeSpan.FromMilliseconds(450), _modList,
                                    _projectStore, _buildService, _launcher, source: ContentSource,
                                    elevation: WindowsElevation.Default);
        _buildView = new BuildView { DataContext = _build };
        _mods = new ModsViewModel(_modList, _nav, _modsService, WindowsElevation.Default);
        _modsView = new ModsView { DataContext = _mods };
        _backups = new BackupsViewModel(new BackupsService(settings.GamePath, workspace.Root), _projectStore,
                                         saves: new SaveBackupsService(SaveBackupsService.DefaultSaveFolder, workspace.Root));
        _backupsView = new BackupsView { DataContext = _backups };
        _settings = new SettingsViewModel(store, scumSources, ContentSource, workspace, nav: _nav);
        _settings.CacheCleared += RebuildItemsAfterClear;   // re-read SCUM now, then say so
        _settings.GameConfigured += ApplyGameConfiguration;  // first-time setup: come alive, no restart
        _settingsView = new SettingsView { DataContext = _settings };
        _helpView = new HelpView();

        Sidebar = new[]
        {
            Nav(ScreenKey.Workspace, "Workspace"),
            Nav(ScreenKey.Home, "Home"),
            Nav(ScreenKey.Projects, "Projects"),
            Nav(ScreenKey.Items, "Items"),
            Nav(ScreenKey.BaseBuilding, "Base building"),
            Nav(ScreenKey.Recipes, "Recipes"),
            Nav(ScreenKey.Build, "Build"),
            Nav(ScreenKey.Mods, "Mods"),
            Nav(ScreenKey.Backups, "Backups"),
            Nav(ScreenKey.Settings, "Settings"),
            Nav(ScreenKey.Help, "Help"),
        };

        // The rail's three labelled bands, built once from the one source of band membership.
        Bands = NavBands.Group(Sidebar)
                        .Select(g => new NavBandSection(g.Band, g.Items))
                        .ToList();
        foreach (var band in Bands)
        {
            band.PropertyChanged += OnBandSelectionChanged;
        }

        // Start on the screen the owner was last using (when it still exists in the rail).
        if (settings.LastScreen is { } last && Enum.TryParse<ScreenKey>(last, out var restored)
            && Sidebar.Any(i => i.Key == restored))
        {
            _nav.GoTo(restored);
        }

        _selectedEntry = Sidebar[0];
        _nav.Changed += Sync;
        _projectStore.Changed += SyncActiveProject;
        SyncActiveProject();
        Sync();

        // Fill Items and Base building with the real data when a game is configured (no-op otherwise).
        _ = _items.LoadFromSourceAsync();
        _ = _baseElements.LoadFromSourceAsync();
        _ = _recipes.LoadFromSourceAsync();
        _ = _workspaceVm.OpenKindAsync(_workspaceVm.SelectedKind);
    }

    /// <summary>The rail entries, Home…Settings in order.</summary>
    public IReadOnlyList<NavItem> Sidebar { get; }

    /// <summary>The rail rows grouped under their three bands (Make · Ship · App), for the rail to show.</summary>
    public IReadOnlyList<NavBandSection> Bands { get; }

    /// <summary>Home's view model (kept alive so the welcome dismissal survives switching areas).</summary>
    public HomeViewModel Home => _home;

    /// <summary>Projects' view model (kept alive so the screen's state survives switching areas).</summary>
    public ProjectsViewModel Projects => _projects;

    /// <summary>Items' view model (kept alive so the screen's state survives switching areas).</summary>
    public ItemsViewModel Items => _items;

    /// <summary>The one store of project change records — what Build will make.</summary>
    public ItemChangeStore ItemChanges => _itemChanges;

    /// <summary>Build's view model (kept alive so the screen's state survives switching areas).</summary>
    public BuildViewModel Build => _build;

    /// <summary>The shared list of installed mods.</summary>
    public ModList ModList => _modList;

    /// <summary>Mods' view model (kept alive so the screen's state survives switching areas).</summary>
    public ModsViewModel Mods => _mods;

    /// <summary>Backups' view model (kept alive so the screen's state survives switching areas).</summary>
    public BackupsViewModel Backups => _backups;

    /// <summary>Settings' view model (kept alive so the screen's state survives switching areas).</summary>
    public SettingsViewModel Settings => _settings;

    /// <summary>The projects the app is working from.</summary>
    public ProjectStore ProjectStore => _projectStore;

    /// <summary>The Workspace's view model (kept alive so the picker state survives switching areas).</summary>
    public WorkspaceViewModel Workspace => _workspaceVm;

    /// <summary>The one content source: reading the player's real SCUM files.</summary>
    public SCMW.Core.Content.IContentSource ContentSource { get; }

    /// <summary>One rail row, with its light/dark icons loaded.</summary>
    private static NavItem Nav(ScreenKey key, string label)
    {
        var (light, dark) = RailIcons.For(key);
        return new NavItem(key, label, light, dark);
    }

    /// <summary>True once the player has told us where SCUM is and given an unlock key.</summary>
    private static bool IsScumConfigured(ISettingsStore store)
    {
        var s = store.Load();
        return !string.IsNullOrWhiteSpace(s.GamePath) && !string.IsNullOrWhiteSpace(s.AesKey);
    }

    /// <summary>Keeps the header's project card in step with the real current project.</summary>
    private void SyncActiveProject()
    {
        var current = _projectStore.Chosen;
        ActiveProjectText = current?.Name ?? "No project open";
        CurrentProject = current is null ? null : ProjectSummary.From(current, true);
    }

    /// <summary>
    /// Rebuilds the game-dependent pieces from the current settings, so a first-time player who sets
    /// SCUM up during the session gets real items, Build, Mods and Launch — with no restart.
    /// </summary>
    internal void ApplyGameConfiguration()
    {
        var settings = _settingsStore.Load();
        var paks = ContentSourceFactory.PakFolderFor(settings, _workspace);
        ContentSource.Configure(paks ?? "", settings.AesKey, settings.BuildId);

        // Release the previous build mount (it holds the game's pak files open) before replacing it.
        (_buildService as IDisposable)?.Dispose();
        _buildService = BuildService.Create(settings, _workspace, ContentSource);
        if (!_launcherInjected) _launcher = new ScumLauncher(settings.GamePath);
        if (!_modsInjected) _modsService = new ModsService(settings.GamePath, _workspace.Root);

        // The Backups screen is built from the game path too — refresh it for first-time setup.
        _backups.UseService(new BackupsService(settings.GamePath, _workspace.Root),
                            new SaveBackupsService(SaveBackupsService.DefaultSaveFolder, _workspace.Root));

        CanLaunch = _launcher.IsAvailable;
        LaunchNote = CanLaunch ? "" : "Set your SCUM folder in Settings to launch.";

        _build.UseGame(_buildService, _launcher);
        _mods.UseService(_modsService);
        _home.RefreshWelcome();                  // the "set up SCUM first" card can retire
        _ = _items.LoadFromSourceAsync();
        _ = _baseElements.LoadFromSourceAsync();
        _ = _recipes.LoadFromSourceAsync();
        _ = _workspaceVm.OpenKindAsync(_workspaceVm.SelectedKind);
    }

    /// <summary>Re-reads SCUM after "Read Game Data", then tells Settings plainly that it is done.</summary>
    private async void RebuildItemsAfterClear()
    {
        try
        {
            await _items.LoadFromSourceAsync();
            await _baseElements.LoadFromSourceAsync();
            await _recipes.LoadFromSourceAsync();
            await _workspaceVm.OpenKindAsync(_workspaceVm.SelectedKind);
            _settings.RefreshFinished(_items.IsUsingRealItems
                ? "Done — SCMW — Lone Wolf re-read your items from SCUM."
                : string.IsNullOrWhiteSpace(_items.SourceNote)
                    ? "Done, but no items were found to read."
                    : "Done. " + _items.SourceNote,
                good: _items.IsUsingRealItems);
        }
        catch
        {
            _settings.RefreshFinished("The re-read didn't finish — check your SCUM folder, then try again.");
        }
    }

    /// <summary>Two-way bound to the rail selection.</summary>
    [ObservableProperty] private NavItem _selectedEntry;

    /// <summary>The area currently shown.</summary>
    [ObservableProperty] private ScreenKey _current = ScreenKey.Home;

    /// <summary>Human-readable title of the active area.</summary>
    [ObservableProperty] private string _screenTitle = "Home";

    /// <summary>The control shown in the content area (non-null from construction).</summary>
    [ObservableProperty] private Control _activeView = new AreaPlaceholderView();

    /// <summary>Which project the header shows as open (its name; "No project open" when none).</summary>
    [ObservableProperty] private string _activeProjectText = "No project open";

    /// <summary>The current project for the header card — its picture, name and description.</summary>
    [ObservableProperty] private ProjectSummary? _currentProject;

    /// <summary>True when a project is open, so the header shows its card instead of the empty line.</summary>
    public bool HasCurrentProject => CurrentProject is not null;

    partial void OnCurrentProjectChanged(ProjectSummary? value) =>
        OnPropertyChanged(nameof(HasCurrentProject));

    /// <summary>True when SCUM's program was found under the configured game path.</summary>
    [ObservableProperty] private bool _canLaunch;

    /// <summary>Plain line under the launch buttons: how to set the game, or what just happened.</summary>
    [ObservableProperty] private string _launchNote =
        "Set your SCUM folder in Settings to launch.";

    /// <summary>True while there is something to tell the player about launching.</summary>
    public bool ShowLaunchNote => !string.IsNullOrWhiteSpace(LaunchNote);

    /// <summary>The app version, shown beside the brand in the header (e.g. "v1.0.0").</summary>
    public string VersionText => $"v{AppInfo.Version}";

    /// <summary>The window title, naming the version.</summary>
    public string WindowTitle => $"{AppInfo.Name} {VersionText}";

    /// <summary>The "on the way" strip's message: the ladder, each level with its plain meaning.</summary>
    public string OnTheWayText => OnTheWay.Message;

    /// <summary>Whether the levels strip is showing (the player can dismiss it for this session).</summary>
    [ObservableProperty] private bool _showOnTheWay = true;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchModded() => await RunLaunchAsync(modded: true);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchUnmodded() => await RunLaunchAsync(modded: false);

    /// <summary>Hides the levels strip for this session (mirrors the Home welcome card).</summary>
    [RelayCommand] private void DismissOnTheWay() => ShowOnTheWay = false;

    /// <summary>Switches the mods on/off as asked, starts SCUM (through Steam), and says what happened.</summary>
    private async Task RunLaunchAsync(bool modded)
    {
        LaunchNote = "";

        if (_modsService is { } mods)
        {
            var switched = await Task.Run(() => mods.SetAllEnabled(modded));
            if (!switched.Ok)
            {
                LaunchNote = switched.Error ?? "The mods could not be switched.";
                return;
            }
        }

        var result = modded
            ? await _launcher.LaunchModdedAsync()
            : await _launcher.LaunchUnmoddedAsync();
        LaunchNote = result.Ok
            ? (modded ? "" : "Mods switched off — SCUM started as vanilla.")
            : (result.Error ?? "SCUM could not be started.");
    }

    partial void OnSelectedEntryChanged(NavItem value) => _nav.GoTo(value.Key);

    /// <summary>A rail row was clicked in one of the bands: make it the current screen.</summary>
    private void OnBandSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavBandSection.Selected))
        {
            return;
        }

        if (sender is NavBandSection band && band.Selected is { } row && !ReferenceEquals(row, SelectedEntry))
        {
            SelectedEntry = row;
        }
    }

    partial void OnCanLaunchChanged(bool value)
    {
        LaunchModdedCommand.NotifyCanExecuteChanged();
        LaunchUnmoddedCommand.NotifyCanExecuteChanged();
    }

    partial void OnLaunchNoteChanged(string value) => OnPropertyChanged(nameof(ShowLaunchNote));

    private void Sync()
    {
        Current = _nav.Current;
        ScreenTitle = Sidebar.First(i => i.Key == Current).Label;

        // Remember the screen so the next launch opens here.
        SaveLastScreen(Current);

        // The mods folder can change outside this screen (a build's Install, a manual copy), so
        // re-read it every time the screen is opened.
        if (Current == ScreenKey.Mods) _mods.Refresh();

        // Backups are made outside this screen (a mod removal, a project delete), so re-read them.
        if (Current == ScreenKey.Backups) _backups.Refresh();

        // Home's first-run card depends on whether SCUM is set up, which may have just changed.
        if (Current == ScreenKey.Home) _home.RefreshWelcome();

        ActiveView = Current switch
        {
            ScreenKey.Workspace => _workspaceView,
            ScreenKey.Home => _homeView,
            ScreenKey.Projects => _projectsView,
            ScreenKey.Items => _itemsView,
            ScreenKey.BaseBuilding => _baseElementsView,
            ScreenKey.Recipes => _recipesView,
            ScreenKey.Build => _buildView,
            ScreenKey.Mods => _modsView,
            ScreenKey.Backups => _backupsView,
            ScreenKey.Settings => _settingsView,
            ScreenKey.Help => _helpView,
            _ => new AreaPlaceholderView { Title = ScreenTitle },
        };

        // Keep exactly one row shown as selected: the band that owns the current screen.
        var entry = Sidebar.First(i => i.Key == Current);
        foreach (var band in Bands)
        {
            band.Selected = band.Items.FirstOrDefault(i => i.Key == Current);
        }

        if (!ReferenceEquals(entry, SelectedEntry))
        {
            SelectedEntry = entry;
        }
    }

    /// <summary>Remembers the current screen so the next launch can reopen on it.</summary>
    private void SaveLastScreen(ScreenKey key)
    {
        try
        {
            var settings = _settingsStore.Load();
            if (string.Equals(settings.LastScreen, key.ToString(), StringComparison.Ordinal))
                return;   // already saved: no needless disk writes

            _settingsStore.Save(settings with { LastScreen = key.ToString() });
        }
        catch
        {
            // A settings hiccup must never break navigation.
        }
    }
}
