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
    private readonly WorkspaceViewModel _workspaceVm;
    private readonly WorkspaceView _workspaceView;
    private readonly BulkTableViewModel _bulk;
    private readonly BulkTableView _bulkView;
    private readonly ProjectsViewModel _projects;
    private readonly ProjectsView _projectsView;
    private readonly ItemChangeStore _itemChanges;
    private readonly INewAssetStore _newAssets;
    private readonly ICraftingSectionStore _sections;
    private readonly ModList _modList;
    private readonly BuildViewModel _build;
    private readonly BuildView _buildView;
    private readonly ISaveBackupsService _saves;
    private readonly ModsViewModel _mods;
    private readonly ModsView _modsView;
    private readonly BackupsViewModel _backups;
    private readonly BackupsView _backupsView;
    private readonly SettingsViewModel _settings;
    private readonly SettingsView _settingsView;
    private readonly HelpView _helpView;
    private IGameLauncher _launcher;
    private IModsService? _modsService;
    private IUe4ssSwitch _ue4ss;
    private readonly bool _ue4ssInjected;
    private IBuildService? _buildService;
    private readonly ISettingsStore _settingsStore;
    private readonly SCMW.Core.Workspace.WorkspacePaths _workspace;
    private readonly bool _launcherInjected;
    private readonly bool _modsInjected;
    public ShellViewModel(INavigation? nav = null, ISettingsStore? settingsStore = null,
                          IScumSources? scumSources = null, ProjectStore? projects = null,
                          SCMW.Core.Content.IContentSource? contentSource = null,
                          IGameLauncher? launcher = null, IModsService? modsService = null,
                          ISaveBackupsService? saves = null, IUe4ssSwitch? ue4ss = null)
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
        _ue4ss = ue4ss ?? new Ue4ssSwitch(settings.GamePath);
        _ue4ssInjected = ue4ss is not null;

        // A clean launch switches UE4SS off; put it back as soon as the app is opened again, so a
        // crash or a forgotten switch can never leave the game without its mod loader.
        _ue4ss.TurnOn();
        CanLaunch = _launcher.IsAvailable;
        LaunchNote = CanLaunch ? "" : "Set your SCUM folder in Settings to launch.";

        _itemChanges = new ItemChangeStore(new ProjectChangeStore(), _projectStore);
        _newAssets = new NewAssetStore();
        _sections = new CraftingSectionStore();
        _workspaceVm = new WorkspaceViewModel(FieldLibraryLoader.LoadDefault(), ContentSource,
                                              new ContentCatalog(ContentSource, ContentSource.Icons),
                                              changes: _itemChanges, projects: _projectStore,
                                              newAssets: _newAssets, sections: _sections);
        _workspaceView = new WorkspaceView { DataContext = _workspaceVm };
        _bulk = new BulkTableViewModel(FieldLibraryLoader.LoadDefault(), ContentSource,
                                       new ContentCatalog(ContentSource, ContentSource.Icons),
                                       changes: _itemChanges, projects: _projectStore);
        _bulkView = new BulkTableView { DataContext = _bulk };
        _projects = new ProjectsViewModel(_projectStore, _nav, _itemChanges,
                                          gameReady: () => IsScumConfigured(store),
                                          source: ContentSource,
                                          openItem: OpenItemInWorkspace);
        _projectsView = new ProjectsView { DataContext = _projects };
        _modList = new ModList();
        _saves = saves ?? new SaveBackupsService(SaveBackupsService.DefaultSaveFolder, workspace.Root);
        _build = new BuildViewModel(_itemChanges, _nav, TimeSpan.FromMilliseconds(450), _modList,
                                    _projectStore, _buildService, source: ContentSource,
                                    elevation: WindowsElevation.Default, saves: _saves,
                                    newAssets: _newAssets, sections: _sections);
        _build.Preview.OpenItemRequested += OpenItemInWorkspace;
        _buildView = new BuildView { DataContext = _build };
        _mods = new ModsViewModel(_modList, _nav, _modsService, WindowsElevation.Default,
                                  projects: _projectStore);
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
            // The top group: the place the app opens on, with no band heading over it.
            Nav(ScreenKey.Projects, "Projects"),

            // MODIFY: change something that already exists.
            Job("Items"),
            Job("Base building"),
            Job("Recipes"),
            Job("Cooking"),

            // MAKE: create something new. The last two are described but not built yet, so they show
            // greyed with "soon" rather than pretending to work.
            Create("Items", "New item"),
            Create("Base building", "New base part", comingSoon: true),
            Create("Recipes", "New crafting recipe"),
            Create("Cooking", "New cooking recipe", comingSoon: true),

            // "Modify in bulk" is hidden from the rail for now — its screen and mechanics stay in
            // place, so dropping the hidden flag brings the row back when we review it.
            Nav(ScreenKey.BulkEdit, "Modify in bulk", hidden: true),
            Nav(ScreenKey.Build, "Build"),
            Nav(ScreenKey.Mods, "Mods Management"),
            Nav(ScreenKey.Backups, "Backups"),
            Nav(ScreenKey.Settings, "Settings"),
            Nav(ScreenKey.Help, "Help"),
        };

        // The rail's three labelled bands, built once from the one source of band membership.
        // A hidden area keeps its screen but gets no rail row.
        Bands = NavBands.Group(Sidebar.Where(i => !i.Hidden))
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

        // The page can change the list on its own (e.g. "Make something new" opens the recipe list),
        // so the rail's lit row follows whatever list is actually open.
        _workspaceVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.SelectedKind)) SyncRailSelection();
        };

        _projectStore.Changed += SyncActiveProject;
        SyncActiveProject();
        Sync();

        // Fill the Workspace and the bulk table with the real data when a game is configured (no-op otherwise).
        _ = _workspaceVm.OpenKindAsync(_workspaceVm.SelectedKind);
        _ = _bulk.OpenKindAsync(_bulk.SelectedKind);
    }

    /// <summary>The rail entries, Home…Settings in order.</summary>
    public IReadOnlyList<NavItem> Sidebar { get; }

    /// <summary>The rail rows grouped under their three bands (Make · Ship · App), for the rail to show.</summary>
    public IReadOnlyList<NavBandSection> Bands { get; }

    /// <summary>The projects the app is working from.</summary>
    public ProjectStore ProjectStore => _projectStore;

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

    /// <summary>Projects' view model (kept alive so the screen's state survives switching areas).</summary>
    public ProjectsViewModel Projects => _projects;

    /// <summary>The Workspace's view model (kept alive so the picker state survives switching areas).</summary>
    public WorkspaceViewModel Workspace => _workspaceVm;

    /// <summary>The one content source: reading the player's real SCUM files.</summary>
    public SCMW.Core.Content.IContentSource ContentSource { get; }

    /// <summary>One rail row, with its light/dark icons loaded.</summary>
    private static NavItem Nav(ScreenKey key, string label, bool hidden = false)
    {
        var (light, dark) = RailIcons.For(key);
        return new NavItem(key, label, light, dark) { Hidden = hidden };
    }

    /// <summary>One of the Workspace's job rows: the job's words and icon, opening that list.</summary>
    private static NavItem Job(string kind)
    {
        var chip = KindChip.All.First(c => string.Equals(c.Key, kind, StringComparison.Ordinal));
        var (light, dark) = RailIcons.ForKind(kind);
        return new NavItem(ScreenKey.Workspace, chip.Label, light, dark) { Kind = kind };
    }

    /// <summary>One of the MAKE rows: open that list and start a brand-new asset of its kind.</summary>
    private static NavItem Create(string kind, string label, bool comingSoon = false)
    {
        var (light, dark) = RailIcons.ForKind(kind);
        return new NavItem(ScreenKey.Workspace, label, light, dark)
        {
            Kind = kind,
            Creates = true,
            ComingSoon = comingSoon,
        };
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
        if (!_ue4ssInjected)
        {
            // Follow the game path when it is first set or changed, and never leave a new install
            // without its mod loader (a previous clean launch may have switched it off).
            _ue4ss = new Ue4ssSwitch(settings.GamePath);
            _ue4ss.TurnOn();
        }

        // The Backups screen is built from the game path too — refresh it for first-time setup.
        _backups.UseService(new BackupsService(settings.GamePath, _workspace.Root),
                            new SaveBackupsService(SaveBackupsService.DefaultSaveFolder, _workspace.Root));

        CanLaunch = _launcher.IsAvailable;
        LaunchNote = CanLaunch ? "" : "Set your SCUM folder in Settings to launch.";

        _build.UseGame(_buildService);
        _mods.UseService(_modsService);
        _projects.RefreshWelcome();              // the "set up SCUM first" card can retire
        _ = _workspaceVm.OpenKindAsync(_workspaceVm.SelectedKind);
        _ = _bulk.OpenKindAsync(_bulk.SelectedKind);
    }

    /// <summary>Re-reads SCUM after "Read Game Data", then tells Settings plainly that it is done.</summary>
    private async void RebuildItemsAfterClear()
    {
        try
        {
            await _workspaceVm.OpenKindAsync(_workspaceVm.SelectedKind);
            await _bulk.OpenKindAsync(_bulk.SelectedKind);

            var ready = ContentSource.Status.State == SCMW.Core.Content.ContentSourceState.Ready;
            _settings.RefreshFinished(ready
                ? "Done — SCMW — Lone Wolf re-read your items from SCUM."
                : "The re-read didn't finish — check your SCUM folder, then try again.",
                good: ready);
        }
        catch
        {
            _settings.RefreshFinished("The re-read didn't finish — check your SCUM folder, then try again.");
        }
    }

    /// <summary>Two-way bound to the rail selection.</summary>
    [ObservableProperty] private NavItem _selectedEntry;

    /// <summary>The area currently shown.</summary>
    [ObservableProperty] private ScreenKey _current = ScreenKey.Projects;

    /// <summary>Human-readable title of the active area.</summary>
    [ObservableProperty] private string _screenTitle = "Projects";

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

    /// <summary>Switches the mods (and UE4SS for a clean launch) as asked, starts SCUM, and says what happened.</summary>
    private async Task RunLaunchAsync(bool modded)
    {
        LaunchNote = "";

        // A modded launch loads the mods, and a pak can leave a mark in a save (design §11), so keep
        // a copy of the saved game first. Best-effort: a failure is said plainly, but never blocks.
        var saveNote = modded ? _saves.BackUp() ?? "" : "";

        if (_modsService is { } mods)
        {
            var switched = await Task.Run(() => mods.SetAllEnabled(modded));
            if (!switched.Ok)
            {
                LaunchNote = switched.Error ?? "The mods could not be switched.";
                return;
            }
        }

        // UE4SS only goes off for a clean launch. Restoring it before a modded launch means the mods
        // really do load, even if an earlier clean session left it switched off.
        var ue4ss = modded ? _ue4ss.TurnOn() : _ue4ss.TurnOff();
        if (!ue4ss.Ok)
        {
            LaunchNote = ue4ss.Error ?? "The mod loader could not be switched.";
            return;
        }

        var result = modded
            ? await _launcher.LaunchModdedAsync()
            : await _launcher.LaunchUnmoddedAsync();
        LaunchNote = result.Ok
            ? (modded ? saveNote : "Mods and UE4SS switched off — SCUM started clean for online play.")
            : (modded
                ? (result.Error ?? "SCUM could not be started.")
                : "Mods and UE4SS are switched off. " + (result.Error ?? "SCUM could not be started."));
    }

    partial void OnSelectedEntryChanged(NavItem value)
    {
        if (!value.CanPick)
        {
            // A "coming soon" idea was clicked: put the highlight back where the open screen is.
            SyncRailSelection();
            return;
        }

        // A job row picks the Workspace list as well as opening the screen.
        if (value.Kind is { } kind) _workspaceVm.SelectedKind = kind;

        _nav.GoTo(value.Key);

        // A MAKE row goes one step further: it starts a brand-new asset of that list's kind.
        if (value is { Creates: true, Kind: { } creating })
            _ = _workspaceVm.StartCreatingAsync(creating);
    }

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
        ScreenTitle = Current == ScreenKey.Workspace
            ? "Workspace"
            : Sidebar.First(i => i.Key == Current).Label;

        // Remember the screen so the next launch opens here.
        SaveLastScreen(Current);

        // The mods folder can change outside this screen (a build's Install, a manual copy), so
        // re-read it every time the screen is opened.
        if (Current == ScreenKey.Mods) _mods.Refresh();

        // Backups are made outside this screen (a mod removal, a project delete, a build's save copy),
        // so re-read both areas every time it is opened.
        if (Current == ScreenKey.Backups) { _backups.Refresh(); _backups.RefreshSaves(); }

        // Projects' first-run card depends on whether SCUM is set up, which may have just changed.
        if (Current == ScreenKey.Projects) _projects.RefreshWelcome();

        // Build re-reads the chosen project's changes (its "What's in this mod" preview) when opened.
        if (Current == ScreenKey.Build) _build.Preview.Refresh();

        // The bulk table re-reads the current kind's files every time it is opened.
        if (Current == ScreenKey.BulkEdit) _ = _bulk.OpenKindAsync(_bulk.SelectedKind);

        ActiveView = Current switch
        {
            ScreenKey.Workspace => _workspaceView,
            ScreenKey.BulkEdit => _bulkView,
            ScreenKey.Projects => _projectsView,
            ScreenKey.Build => _buildView,
            ScreenKey.Mods => _modsView,
            ScreenKey.Backups => _backupsView,
            ScreenKey.Settings => _settingsView,
            ScreenKey.Help => _helpView,
            _ => new AreaPlaceholderView { Title = ScreenTitle },
        };

        // Keep exactly one row shown as selected: the band that owns the current screen, and — for
        // the Workspace's four job rows — the one for the list being browsed.
        SyncRailSelection();
    }

    /// <summary>Lights up the one rail row that matches the open screen (and, for the Workspace,
    /// the list being browsed).</summary>
    private void SyncRailSelection()
    {
        var entry = Sidebar.First(i => i.Key == Current && MatchesKind(i));
        foreach (var band in Bands)
        {
            band.Selected = band.Items.FirstOrDefault(i => i.Key == Current && MatchesKind(i));
        }

        if (!ReferenceEquals(entry, SelectedEntry))
        {
            SelectedEntry = entry;
        }
    }

    /// <summary>True for a row with no list of its own, or the modify row whose list is open. The MAKE
    /// rows share the same list but are never the lit one.</summary>
    private bool MatchesKind(NavItem item) =>
        !item.Creates
        && (item.Kind is null || string.Equals(item.Kind, _workspaceVm.SelectedKind, StringComparison.Ordinal));

    /// <summary>Opens one of the chosen mod's items in the Workspace, ready to edit (the Projects
    /// screen's change cards call this).</summary>
    private void OpenItemInWorkspace(string assetPath) => _ = OpenItemInWorkspaceAsync(assetPath);

    private async Task OpenItemInWorkspaceAsync(string assetPath)
    {
        if (await _workspaceVm.OpenAssetByPathAsync(assetPath).ConfigureAwait(true))
            _nav.GoTo(ScreenKey.Workspace);
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
