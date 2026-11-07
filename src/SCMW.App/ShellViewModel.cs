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
        _modList = new ModList();
        _saves = saves ?? new SaveBackupsService(SaveBackupsService.DefaultSaveFolder, workspace.Root);
        _mods = new ModsViewModel(_modList, _nav, _modsService, WindowsElevation.Default,
                                  projects: _projectStore);
        _build = new BuildViewModel(_itemChanges, _nav, TimeSpan.FromMilliseconds(450), _modList,
                                    _projectStore, _buildService, source: ContentSource,
                                    elevation: WindowsElevation.Default, saves: _saves,
                                    newAssets: _newAssets, sections: _sections);
        _build.Preview.OpenItemRequested += OpenItemInWorkspace;

        // Build's brain is hosted by Projects: each project card builds its own mod there.
        // The card's own switch reads and asks the Mods screen, so both switches share one behaviour.
        _projects = new ProjectsViewModel(_projectStore, _nav, _itemChanges,
                                          gameReady: () => IsScumConfigured(store),
                                          source: ContentSource,
                                          openItem: OpenItemInWorkspace,
                                          build: _build,
                                          modOn: name => _mods.ForProject(name) is { } mod ? mod.IsOn : null,
                                          setModOn: (name, on) => _mods.SetProjectOn(name, on),
                                          refreshMods: () => _mods.Refresh());
        _projectsView = new ProjectsView { DataContext = _projects };
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

        // The page can change the list on its own (e.g. a MAKE row opens a different list),
        // so the rail's lit row follows whatever list is actually open.
        _workspaceVm.PropertyChanged += (_, e) =>
        {
            // The panel opening ends "starting a new thing"; its shutting on the way there does not, so
            // the light is held (see SyncRailSelection) instead of blinking back to the Modify row.
            if (e.PropertyName == nameof(WorkspaceViewModel.ShowNewRecipe) && _workspaceVm.ShowNewRecipe)
                _startingCreation = false;

            if (e.PropertyName is nameof(WorkspaceViewModel.SelectedKind) or nameof(WorkspaceViewModel.ShowNewRecipe))
                SyncRailSelection();
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

    /// <summary>
    /// The MAKE row whose New panel is open ("New item", "New crafting recipe"), so that row can be the
    /// lit one while the owner is making something. Null when no panel is open: a MAKE row is a command,
    /// and never the lit row otherwise.
    /// </summary>
    private NavItem? _creatingRow;

    /// <summary>True while a MAKE click is being carried out, so the syncs it causes on the way are held
    /// back and the light lands on that row once, instead of stopping on the Modify row first.</summary>
    private bool _creating;

    /// <summary>True from a MAKE click until its panel opens: switching the list shuts the panel for a
    /// moment, and the light must wait through that rather than blink back to the Modify row.</summary>
    private bool _startingCreation;

    /// <summary>True while the rail is moving its own light, so that move is not read as the owner
    /// picking a row (which would drop the MAKE row's claim).</summary>
    private bool _syncing;

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

        // A clean launch switches every mod off. A modded launch leaves the player's own switches
        // exactly as they set them — they may deliberately keep a clashing mod off.
        if (_modsService is { } mods && !modded)
        {
            var switched = await Task.Run(() => mods.SetAllEnabled(false));
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

        var isMake = value.Creates && value.Kind is not null;
        var closeNewPanel = false;

        // A MAKE row owns the light while its panel is open, so it takes it straight away: the syncs
        // this click causes on the way (a list switch, the screen change) are held back, or the light
        // would stop on the Modify row for a moment first — a visible jump the owner would see.
        if (isMake)
        {
            _creatingRow = value;
            _startingCreation = true;
        }
        else if (!_syncing)
        {
            // A real pick of another row: the MAKE row gives up its claim, and any New form left
            // half-made goes with it, so the plain invitation is what the owner comes back to.
            _creatingRow = null;
            _startingCreation = false;
            closeNewPanel = _workspaceVm.ShowNewRecipe;
        }

        _creating = isMake;
        try
        {
            // A job row picks the Workspace list as well as opening the screen.
            if (value.Kind is { } kind) _workspaceVm.SelectedKind = kind;

            _nav.GoTo(value.Key);

            // The screen and list are right by now, so the rail's light lands in the right place
            // when the panel closes.
            if (closeNewPanel) _workspaceVm.CancelNewRecipeCommand.Execute(null);

            // A MAKE row goes one step further: it starts a brand-new asset of that list's kind.
            if (isMake) _ = StartCreatingAsync(value, value.Kind!);
        }
        finally
        {
            _creating = false;
        }

        // Always leave exactly one row lit. A MAKE row with no panel open is a command, never the lit
        // row, and a click on the row for the list that is ALREADY open changes no property and no
        // screen — without this, that click would light it beside the Modify row and then do nothing.
        SyncRailSelection();
    }

    /// <summary>
    /// Starts a brand-new asset from a MAKE row. If the panel never opens (no list to start from), the
    /// claim on the light is dropped and the light goes back where it belongs.
    /// </summary>
    private async Task StartCreatingAsync(NavItem row, string kind)
    {
        _workspaceVm.KeepingNewPanel = true;
        try
        {
            await _workspaceVm.StartCreatingAsync(kind).ConfigureAwait(true);
        }
        finally
        {
            _workspaceVm.KeepingNewPanel = false;

            if (!_workspaceVm.ShowNewRecipe)
            {
                _startingCreation = false;
                if (ReferenceEquals(_creatingRow, row)) _creatingRow = null;
                SyncRailSelection();
            }
        }
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

        // Leaving the Workspace ends any MAKE row's turn: only its own screen has the panel.
        if (Current != ScreenKey.Workspace) _creatingRow = null;
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
        // The cards' mod switches also follow the mods folder, which the Mods screen may have changed.
        if (Current == ScreenKey.Projects) { _projects.RefreshWelcome(); _projects.RefreshModStates(); }

        // Build re-reads the chosen project's changes (its "What's in this mod" preview) when opened.
        if (Current == ScreenKey.Projects) _build.Preview.Refresh();

        // The bulk table re-reads the current kind's files every time it is opened.
        if (Current == ScreenKey.BulkEdit) _ = _bulk.OpenKindAsync(_bulk.SelectedKind);

        ActiveView = Current switch
        {
            ScreenKey.Workspace => _workspaceView,
            ScreenKey.BulkEdit => _bulkView,
            ScreenKey.Projects => _projectsView,
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
        // A MAKE click is being carried out, or its panel is shut on the way to opening: that row
        // decides the light, so nothing moves until the panel is open (no blink on the Modify row).
        if (_creating || (_startingCreation && !_workspaceVm.ShowNewRecipe)) return;

        var entry = Sidebar.First(i => i.Key == Current && MatchesKind(i));

        // Everything below is the rail moving its own light — including the band rows, whose change
        // travels back through OnBandSelectionChanged — so none of it counts as the owner picking
        // another row (which would drop a MAKE row's claim on the light).
        _syncing = true;
        try
        {
            foreach (var band in Bands)
            {
                band.Selected = band.Items.FirstOrDefault(i => i.Key == Current && MatchesKind(i));
            }

            if (!ReferenceEquals(entry, SelectedEntry))
            {
                SelectedEntry = entry;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>True for the one row that should be lit: the MAKE row whose New panel is open for the
    /// list being browsed (the owner just clicked it, so the light stays there), else the Modify row for
    /// that list. A MAKE row is never the lit one otherwise — it is a command, not a place.</summary>
    private bool MatchesKind(NavItem item) =>
        Current == ScreenKey.Workspace && _workspaceVm.ShowNewRecipe && _creatingRow is { } creating
        && string.Equals(creating.Kind, _workspaceVm.SelectedKind, StringComparison.Ordinal)
            ? ReferenceEquals(item, creating)
            : !item.Creates
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
