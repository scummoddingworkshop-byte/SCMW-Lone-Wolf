using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    private ProjectSummary? _editing;
    private IReadOnlyList<ProjectSummary> _rows = Array.Empty<ProjectSummary>();

    public ProjectsViewModel(ProjectStore? store = null, INavigation? nav = null)
    {
        _store = store ?? ProjectStore.Default();
        _nav = nav ?? NavigationHost.Shared;
        _store.Changed += Refresh;

        VisibleProjects = new ObservableCollection<ProjectSummary>();
        Refresh();
    }

    /// <summary>The projects currently shown (the filtered view of the workspace).</summary>
    public ObservableCollection<ProjectSummary> VisibleProjects { get; }

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
        var currentId = _store.Current?.Id;

        _rows = _store.All()
            .Select(record => ProjectSummary.From(record, record.Id == currentId))
            .ToList();

        var query = Search.Trim();
        VisibleProjects.Clear();
        foreach (var row in _rows.Where(p =>
                     query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleProjects.Add(row);
        }

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(ShowGrid));
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

        _store.SetCurrent(project.Id);
        _nav.GoTo(ScreenKey.Items);
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
