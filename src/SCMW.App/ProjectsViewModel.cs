using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Core.Editing;
using SCMW.Core.Projects;

namespace SCMW.App;

/// <summary>
/// Projects' brain: the real project list from the workspace, the search filter, the
/// create/rename panel (with plain-worded errors), and the per-project commands. It knows
/// nothing about pixels.
/// </summary>
public partial class ProjectsViewModel : ObservableObject
{
    private readonly ProjectStore _store;
    private readonly INavigation _nav;
    private readonly ItemChangeStore? _changes;
    private readonly BuildViewModel? _build;
    private readonly Func<bool>? _gameReady;
    private readonly SCMW.Core.Content.IContentSource? _source;
    private readonly Action<string>? _openItem;
    private readonly Func<string, bool?>? _modOn;
    private readonly Func<string, bool, string?>? _setModOn;
    private readonly Action? _refreshMods;
    private bool _welcomeDismissed;
    private ProjectSummary? _editing;
    private IReadOnlyList<ProjectSummary> _rows = Array.Empty<ProjectSummary>();

    public ProjectsViewModel(ProjectStore? store = null, INavigation? nav = null, ItemChangeStore? changes = null,
                             Func<bool>? gameReady = null, SCMW.Core.Content.IContentSource? source = null,
                             Action<string>? openItem = null, BuildViewModel? build = null,
                             Func<string, bool?>? modOn = null, Func<string, bool, string?>? setModOn = null,
                             Action? refreshMods = null)
    {
        _store = store ?? ProjectStore.Default();
        _nav = nav ?? NavigationHost.Shared;
        _changes = changes;
        _build = build;
        _gameReady = gameReady;
        _source = source;
        _openItem = openItem;
        _modOn = modOn;
        _setModOn = setModOn;
        _refreshMods = refreshMods;
        _store.Changed += Refresh;
        if (_changes is not null) _changes.Changed += Refresh;
        if (_build is not null) _build.PropertyChanged += OnBuildChanged;

        VisibleProjects = new ObservableCollection<ProjectSummary>();
        Refresh();
        RefreshWelcome();
    }

    /// <summary>Whether the "set SCUM up first" card is showing (first run, game not configured yet).</summary>
    [ObservableProperty] private bool _showWelcome;

    /// <summary>
    /// Re-decides the welcome card: it nudges setup until the game is configured, and stays hidden
    /// once it is (or once the player has dismissed it this session).
    /// </summary>
    public void RefreshWelcome() => ShowWelcome = !_welcomeDismissed && !(_gameReady?.Invoke() ?? false);

    /// <summary>The first-run card sends the player to Settings (game folder + unlock key).</summary>
    [RelayCommand] private void StartWelcome() => _nav.GoTo(ScreenKey.Settings);

    [RelayCommand]
    private void DismissWelcome()
    {
        _welcomeDismissed = true;
        ShowWelcome = false;
    }

    /// <summary>The projects currently shown (the filtered view of the workspace).</summary>
    public ObservableCollection<ProjectSummary> VisibleProjects { get; }

    /// <summary>The chosen project's recorded changes, one card per item (empty when none is chosen).</summary>
    public ObservableCollection<ProjectChangeGroup> Changes { get; } = new();

    /// <summary>Build's brain, hosted here: each project card builds its own mod on this screen.</summary>
    public BuildViewModel? Build => _build;

    /// <summary>True when this screen carries the build controls (a build is wired in).</summary>
    public bool HasBuild => _build is not null;

    private void OnBuildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BuildViewModel.State) or nameof(BuildViewModel.HasChanges)))
            return;

        // A finished build puts the mod into SCUM switched on, so the cards re-read the mods
        // before the owner sees them (owner, 2026-11-07).
        if (e.PropertyName == nameof(BuildViewModel.State) && _build?.State == BuildState.Succeeded)
        {
            _refreshMods?.Invoke();
            Refresh();
            return;
        }

        SyncBuildState();
    }

    /// <summary>Shows a card what the app says about that project's mod: on, off, or none yet.</summary>
    private void ApplyModState(ProjectSummary row)
    {
        var on = _modOn?.Invoke(row.Name);
        row.SetModState(on is not null, on ?? false);
    }

    /// <summary>Re-reads every card's mod state — the Mods screen may have changed it.</summary>
    public void RefreshModStates()
    {
        foreach (var row in _rows) ApplyModState(row);
    }

    /// <summary>The owner flipped a card's switch: ask the app, then show whatever it answers.</summary>
    private void SetMod(ProjectSummary row, bool on)
    {
        if (_setModOn is null)
        {
            ApplyModState(row);
            return;
        }

        ListMessage = _setModOn(row.Name, on) ?? "";

        // A refusal puts the file back, so the switch must follow the app, not the click.
        ApplyModState(row);
    }

    /// <summary>Keeps every card's Build button honest: on when it has something to build and nothing
    /// else is running.</summary>
    private void SyncBuildState()
    {
        var running = _build?.State == BuildState.Running;
        foreach (var row in _rows)
        {
            row.ShowBuild = _build is not null;
            row.CanBuild = _build is not null && row.HasChanges && !running;
        }
    }

    /// <summary>How many changes there are in total, across every item.</summary>
    private int ChangeCount => Changes.Sum(g => g.Rows.Count);

    /// <summary>True when the chosen project has at least one change.</summary>
    public bool HasChanges => ChangeCount > 0;

    /// <summary>True when the player has chosen a project (the one this screen's list belongs to).</summary>
    public bool HasChosenProject => _store.HasChosenProject;

    /// <summary>Plain words for how many changes the chosen project holds.</summary>
    public string ChangeSummary => ChangeCount switch
    {
        0 => "No changes yet",
        1 => "1 change",
        _ => $"{ChangeCount} changes",
    };

    /// <summary>Search text; filters the list live.</summary>
    [ObservableProperty] private string _search = "";

    /// <summary>True while the create/rename panel is showing.</summary>
    [ObservableProperty] private bool _isPanelOpen;

    /// <summary>True when the panel is renaming an existing project.</summary>
    [ObservableProperty] private bool _isRenaming;

    [ObservableProperty] private string _draftName = "";
    [ObservableProperty] private string _draftDescription = "";

    /// <summary>A picture the player just chose for the panel (null = none chosen).</summary>
    [ObservableProperty] private string? _draftIconPath;

    /// <summary>True when the player pressed "Remove picture" on an existing project.</summary>
    [ObservableProperty] private bool _draftIconRemoved;

    /// <summary>What the panel shows in the picture slot (chosen, existing, or none).</summary>
    [ObservableProperty] private IImage? _draftPicture;

    /// <summary>Plain message from the engine when something was refused (empty when fine).</summary>
    [ObservableProperty] private string _panelError = "";

    /// <summary>Plain message for a refusal from a list action (Duplicate/Delete), shown even when
    /// the create/rename panel is closed — the panel's own message would be invisible there.</summary>
    [ObservableProperty] private string _listMessage = "";

    public bool ShowListMessage => ListMessage.Length > 0;

    partial void OnListMessageChanged(string value) => OnPropertyChanged(nameof(ShowListMessage));

    /// <summary>Panel heading — changes when the panel is renaming rather than creating.</summary>
    public string PanelTitle => IsRenaming ? "Edit project" : "New project";

    /// <summary>Panel confirm button label — "Save" when renaming, else "Create".</summary>
    public string PanelAction => IsRenaming ? "Save" : "Create";

    partial void OnIsRenamingChanged(bool value)
    {
        OnPropertyChanged(nameof(PanelTitle));
        OnPropertyChanged(nameof(PanelAction));
    }

    public bool HasProjects => _rows.Count > 0;
    public bool HasMatches => VisibleProjects.Count > 0;
    public bool ShowEmptyState => !HasProjects;
    public bool ShowNoMatches => HasProjects && !HasMatches;
    public bool ShowGrid => HasMatches;

    partial void OnSearchChanged(string value) => Refresh();

    private void Refresh()
    {
        var chosen = _store.Chosen;

        _rows = _store.All()
            .Select(record =>
            {
                var row = ProjectSummary.From(record, record.Id == chosen?.Id, ChangeCountFor(record));
                row.ModToggleRequested = SetMod;
                ApplyModState(row);
                return row;
            })
            .ToList();
        SyncBuildState();

        var query = Search.Trim();
        VisibleProjects.Clear();
        foreach (var row in _rows.Where(p =>
                     query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleProjects.Add(row);
        }

        RefreshChanges(chosen);

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(ShowGrid));
    }

    /// <summary>How many changes a project holds — read from its own change list, one per value
    /// (an old edit to a value that has since been changed again is not counted twice).</summary>
    private int ChangeCountFor(ProjectRecord record) =>
        _changes is null
            ? 0
            : ChangeDiff.Build(_changes.For(record.FolderPath)).Sum(item => item.Values.Count);

    /// <summary>Show the chosen project's change list, one card per item, in plain words (nothing when
    /// none is chosen). The list is the same one Build shows, in the same order: one line per value,
    /// cards and lines alphabetical.</summary>
    private void RefreshChanges(ProjectRecord? chosen)
    {
        Changes.Clear();
        if (chosen is not null && _changes is not null)
        {
            foreach (var item in ChangeDiff.Build(_changes.For(chosen.FolderPath)))
            {
                Changes.Add(new ProjectChangeGroup(
                    item.Values.Select(value => new ProjectChangeRow(item.Path, value)).ToList(),
                    ContentCatalog.FriendlyItemName(item.Path),
                    LoadIcon(item.Path)));
            }
        }

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasChosenProject));
        OnPropertyChanged(nameof(ChangeSummary));
    }

    /// <summary>The item's cached icon, trying its class file ("X" for "X_ES") too. Never throws.</summary>
    private IImage? LoadIcon(string assetId)
    {
        try
        {
            var path = _source?.Icons.GetIconPath(assetId);
            if (path is null && !assetId.EndsWith("_ES", StringComparison.OrdinalIgnoreCase))
                path = _source?.Icons.GetIconPath(assetId + "_ES");

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return new Bitmap(path);
        }
        catch
        {
            return null;   // no icon is fine — the card shows its fallback mark
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        _editing = null;
        DraftName = "My first mod";
        DraftDescription = "";
        DraftIconPath = null;
        DraftIconRemoved = false;
        DraftPicture = null;
        PanelError = "";
        IsRenaming = false;
        IsPanelOpen = true;
    }

    [RelayCommand]
    private void RenameProject(ProjectSummary? project)
    {
        if (project is null) return;

        _editing = project;
        DraftName = project.Name;
        DraftDescription = project.Description;
        DraftIconPath = null;
        DraftIconRemoved = false;
        DraftPicture = project.Picture;
        PanelError = "";
        IsRenaming = true;
        IsPanelOpen = true;
    }

    /// <summary>Records a picture the player picked in the panel (the view opens the file picker).</summary>
    public void SetDraftIcon(string path)
    {
        DraftIconPath = path;
        DraftIconRemoved = false;
        DraftPicture = LoadPicture(path);
    }

    /// <summary>Clears the panel's picture (marks an existing project's picture for removal).</summary>
    [RelayCommand]
    private void RemovePicture()
    {
        DraftIconPath = null;
        DraftIconRemoved = true;
        DraftPicture = null;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editing = null;
        PanelError = "";
        IsPanelOpen = false;
    }

    [RelayCommand]
    private void CommitEdit()
    {
        var name = DraftName.Trim();
        if (name.Length == 0)
        {
            PanelError = "Enter a name for the project.";
            return;
        }

        string? error;
        string? iconWarning = null;

        if (_editing is null)
        {
            var result = _store.Create(name, DraftDescription, DraftIconPath);
            error = result.ErrorMessage;
            iconWarning = result.IconWarning;

            // A new project is where you work, so it becomes the current one.
            if (result.Project is not null) _store.SetCurrent(result.Project.Id);
        }
        else
        {
            error = _store.Rename(_editing.Id, name) ?? _store.SetDescription(_editing.Id, DraftDescription);

            // Only touch the picture when the player chose a new one or asked to remove it.
            if (error is null && (DraftIconRemoved || DraftIconPath is not null))
                iconWarning = _store.SetIcon(_editing.Id, DraftIconRemoved ? null : DraftIconPath);
        }

        if (error is not null)
        {
            PanelError = error;
            return;
        }

        _editing = null;
        PanelError = "";
        IsPanelOpen = false;
        ListMessage = iconWarning ?? "";
        Refresh();
    }

    /// <summary>Loads a chosen picture for the panel preview; a bad file just shows nothing.</summary>
    private static IImage? LoadPicture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return new Bitmap(path); }
        catch { return null; }
    }

    [RelayCommand]
    private void OpenProject(ProjectSummary? project)
    {
        if (project is null) return;

        // Picking a project makes it the one you work on, and its changes appear below — the screen
        // stays where it is, so choosing a mod never throws you into the Workspace.
        _store.SetCurrent(project.Id);
    }

    /// <summary>Opens one of this mod's items in the Workspace, ready to edit it. The shell does the
    /// walking; this screen only says which item was asked for.</summary>
    [RelayCommand]
    private void OpenItem(ProjectChangeGroup? group)
    {
        if (group is null || string.IsNullOrEmpty(group.Path)) return;

        _openItem?.Invoke(group.Path);
    }

    /// <summary>Undo one value on one item where it is shown — removes every recorded edit behind that
    /// line, so the value goes back to the game's own number and the line disappears.</summary>
    [RelayCommand]
    private void UndoChange(ProjectChangeRow? row)
    {
        var chosen = _store.Chosen;
        if (row is null || chosen is null || _changes is null) return;

        _changes.UndoField(chosen.FolderPath, row.Path, row.FieldTechnical);   // raises Changed → Refresh rebuilds
    }

    /// <summary>Build one project straight from its card. The mod being built becomes the current
    /// one (so the list below shows it), and its own remembered version is used.</summary>
    [RelayCommand]
    private void BuildProject(ProjectSummary? project)
    {
        if (project is null || _build is null) return;

        var version = string.IsNullOrWhiteSpace(project.ModVersion)
            ? ProjectService.DefaultModVersion
            : project.ModVersion.Trim();
        project.ModVersion = version;

        _store.SetVersion(project.Id, version);   // remembered with the project, no card jump
        if (!string.Equals(_store.Chosen?.Id, project.Id, StringComparison.Ordinal))
            _store.SetCurrent(project.Id);

        _build.ModVersion = version;
        if (_build.BuildCommand.CanExecute(null)) _build.BuildCommand.Execute(null);
    }

    /// <summary>Drop every change to one item — what Build's own cards used to offer.</summary>
    [RelayCommand]
    private void RemoveItem(ProjectChangeGroup? group)
    {
        var chosen = _store.Chosen;
        if (group is null || chosen is null || _changes is null) return;

        _changes.RemoveItem(chosen.FolderPath, group.Path);   // raises Changed → the card and count refresh
    }

    [RelayCommand]
    private void DuplicateProject(ProjectSummary? project)
    {
        if (project is null) return;

        ListMessage = "";
        var result = _store.Duplicate(project.Id);
        if (result.ErrorMessage is not null) ListMessage = result.ErrorMessage;
    }

    [RelayCommand]
    private void RequestDelete(ProjectSummary? project) => SetConfirming(project, true);

    [RelayCommand]
    private void CancelDelete(ProjectSummary? project) => SetConfirming(project, false);

    [RelayCommand]
    private void ConfirmDelete(ProjectSummary? project)
    {
        if (project is null) return;

        project.IsConfirmingDelete = false;
        ListMessage = "";
        var error = _store.Delete(project.Id);
        if (error is not null) ListMessage = error;
    }

    private static void SetConfirming(ProjectSummary? project, bool value)
    {
        if (project is not null) project.IsConfirmingDelete = value;
    }
}

/// <summary>
/// One line of an item's change card: one value, in plain words, exactly as Build's list shows it.
/// Built from the shared <see cref="BuildValueChange"/>, never a second reading of the records.
/// </summary>
public sealed class ProjectChangeRow
{
    public ProjectChangeRow(string itemPath, BuildValueChange value)
    {
        Path = itemPath;
        Field = value.ValueName;
        FieldTechnical = value.FieldTechnical;

        // The project card says what the value WILL BE — the game's old number is not needed here.
        // The Modify screens keep showing it ("was 10") beside the value being edited.
        Value = value.NewValue;
    }

    /// <summary>The item's game path — what the shell opens, and how Undo finds the edits.</summary>
    public string Path { get; }

    /// <summary>The field that changed, in the words the Modify screen uses ("Height in grid rows").</summary>
    public string Field { get; }

    /// <summary>The field's own game name ("GridInventoryRowSpan"), kept for the tooltip.</summary>
    public string FieldTechnical { get; }

    /// <summary>The value it will have ("75"), or a plain word for a list change ("added").</summary>
    public string Value { get; }

    /// <summary>The file's own name (the last part of its path), e.g. <c>Nails_ES</c>.</summary>
    public string FileName => SCMW.Core.Content.AssetNames.FileName(Path);

    /// <summary>One plain line: "Duct Tape · Max stack → 25".</summary>
    public string Summary => $"{FileName} · {Field} {Value}";
}

/// <summary>
/// One card in the Projects screen's change list: an item and every change recorded on it, laid out
/// the same way as Build's "What's in this mod" so the two screens read alike.
/// </summary>
public sealed class ProjectChangeGroup
{
    public ProjectChangeGroup(IReadOnlyList<ProjectChangeRow> rows, string friendlyName, IImage? picture = null)
    {
        Rows = rows;
        FriendlyName = friendlyName;
        Picture = picture;
    }

    /// <summary>Every change recorded on this item, in the order they were recorded.</summary>
    public IReadOnlyList<ProjectChangeRow> Rows { get; }

    /// <summary>The item's friendly name ("Nails"), the same words Build's list uses.</summary>
    public string FriendlyName { get; }

    /// <summary>The item's picture once found; null shows the fallback mark.</summary>
    public IImage? Picture { get; }

    /// <summary>True when the card has a picture to draw (else the fallback mark shows).</summary>
    public bool HasPicture => Picture is not null;

    /// <summary>The file's own name (the last part of its game path), e.g. <c>Nails_ES</c>.</summary>
    public string FileName => Rows[0].FileName;

    /// <summary>The item's full game path — what the shell opens when the card is clicked.</summary>
    public string Path => Rows[0].Path;

    /// <summary>How many changes this item holds, in plain words.</summary>
    public string ChangeCountText => Rows.Count == 1 ? "1 change" : $"{Rows.Count} changes";
}
