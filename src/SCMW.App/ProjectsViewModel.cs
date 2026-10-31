using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly Func<bool>? _gameReady;
    private bool _welcomeDismissed;
    private ProjectSummary? _editing;
    private IReadOnlyList<ProjectSummary> _rows = Array.Empty<ProjectSummary>();

    public ProjectsViewModel(ProjectStore? store = null, INavigation? nav = null, ItemChangeStore? changes = null,
                             Func<bool>? gameReady = null)
    {
        _store = store ?? ProjectStore.Default();
        _nav = nav ?? NavigationHost.Shared;
        _changes = changes;
        _gameReady = gameReady;
        _store.Changed += Refresh;
        if (_changes is not null) _changes.Changed += Refresh;

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
            .Select(record => ProjectSummary.From(record, record.Id == chosen?.Id, ChangeCountFor(record)))
            .ToList();

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

    /// <summary>How many changes a project holds — read from its own change list.</summary>
    private int ChangeCountFor(ProjectRecord record) =>
        _changes?.For(record.FolderPath).Count ?? 0;

    /// <summary>Show the chosen project's change list, one card per item, in plain words (nothing when
    /// none is chosen).</summary>
    private void RefreshChanges(ProjectRecord? chosen)
    {
        Changes.Clear();
        if (chosen is not null && _changes is not null)
        {
            var rows = _changes.For(chosen.FolderPath)
                               .Select(record => new ProjectChangeRow(record))
                               .GroupBy(row => row.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var group in rows)
                Changes.Add(new ProjectChangeGroup(group.ToList()));
        }

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasChosenProject));
        OnPropertyChanged(nameof(ChangeSummary));
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

        // Choosing a project is deliberate: it becomes the mod you edit, in the Workspace.
        _store.SetCurrent(project.Id);
        _nav.GoTo(ScreenKey.Workspace);
    }

    /// <summary>Undo one recorded change (Task 4.2) — removes exactly that record, nothing else.</summary>
    [RelayCommand]
    private void UndoChange(ProjectChangeRow? row)
    {
        var chosen = _store.Chosen;
        if (row is null || chosen is null || _changes is null) return;

        _changes.Undo(chosen.FolderPath, row.Id);   // raises Changed → Refresh rebuilds the list
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
/// One recorded change, in plain words, for the Projects screen's change list. Kept tiny — it is a
/// reading of the engine's <see cref="ChangeRecord"/>, never a second source of truth.
/// </summary>
public sealed class ProjectChangeRow
{
    public ProjectChangeRow(ChangeRecord record)
    {
        Id = record.Id;
        Asset = record.AssetId;
        Path = string.IsNullOrEmpty(record.AssetPath) ? record.AssetId : record.AssetPath;
        Field = record.FieldTechnicalName;
        Value = record.Operation switch
        {
            ChangeOperation.Multiply => "×" + record.NewValue,
            ChangeOperation.AddEntry => "added an entry",
            ChangeOperation.RemoveEntry => "removed an entry",
            _ => "→ " + record.NewValue,
        };
    }

    /// <summary>The record's id (used later for per-change undo).</summary>
    public Guid Id { get; }

    /// <summary>The item or file the change lands on.</summary>
    public string Asset { get; }

    /// <summary>The asset's game path when it has one (else its recorded id).</summary>
    public string Path { get; }

    /// <summary>The field that changed.</summary>
    public string Field { get; }

    /// <summary>The change in plain words ("→ 25", "×2", "added an entry").</summary>
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
    public ProjectChangeGroup(IReadOnlyList<ProjectChangeRow> rows) => Rows = rows;

    /// <summary>Every change recorded on this item, in the order they were recorded.</summary>
    public IReadOnlyList<ProjectChangeRow> Rows { get; }

    /// <summary>The file's own name (the last part of its game path), e.g. <c>Nails_ES</c>.</summary>
    public string FileName => Rows[0].FileName;

    /// <summary>How many changes this item holds, in plain words.</summary>
    public string ChangeCountText => Rows.Count == 1 ? "1 change" : $"{Rows.Count} changes";
}
