using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCMW.App;

/// <summary>One backup as shown on screen: what it is, when it was kept, and whether it is asking to be deleted.</summary>
public partial class BackupRow : ObservableObject
{
    public BackupRow(BackupEntry entry) => Entry = entry;

    public BackupEntry Entry { get; }

    public string DisplayName => Entry.DisplayName;

    /// <summary>"Mod" or "Project".</summary>
    public string KindText => Entry.Kind == BackupKind.Mod ? "Mod" : "Project";

    /// <summary>Plain "2026-10-08 10:11", or empty when the name carried no stamp.</summary>
    public string When => Entry.WhenUtc == DateTime.MinValue
        ? ""
        : Entry.WhenUtc.ToString("yyyy-MM-dd HH:mm");

    /// <summary>True while this row's inline delete confirmation is showing.</summary>
    [ObservableProperty] private bool _isConfirmingDelete;
}

/// <summary>
/// Backups' brain: the removed mods and deleted projects SCMW — Lone Wolf keeps in the workspace, with
/// one-click restore and delete. It knows nothing about pixels.
/// </summary>
public partial class BackupsViewModel : ObservableObject
{
    private readonly IBackupsService? _service;
    private readonly ProjectStore? _projects;
    private readonly IFolderOpener _folders;

    public BackupsViewModel(IBackupsService? service = null, ProjectStore? projects = null,
                            IFolderOpener? folderOpener = null)
    {
        _service = service;
        _projects = projects;
        _folders = folderOpener ?? new SystemFolderOpener();

        Backups = new ObservableCollection<BackupRow>();
        Refresh();
    }

    /// <summary>The backups currently shown, newest first.</summary>
    public ObservableCollection<BackupRow> Backups { get; }

    /// <summary>A plain line for the last thing that happened (a restore, a delete, a refusal).</summary>
    [ObservableProperty] private string _message = "";

    public bool HasBackups => Backups.Count > 0;
    public bool ShowEmptyState => !HasBackups;

    public string Summary => $"{Backups.Count} {(Backups.Count == 1 ? "backup" : "backups")} kept";

    /// <summary>Re-reads the backups from disk. Called at construction and after any change.</summary>
    public void Refresh()
    {
        Backups.Clear();
        if (_service is not null)
        {
            foreach (var entry in _service.List()) Backups.Add(new BackupRow(entry));
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void RequestDelete(BackupRow? row)
    {
        if (row is not null) row.IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete(BackupRow? row)
    {
        if (row is not null) row.IsConfirmingDelete = false;
    }

    [RelayCommand]
    private void Restore(BackupRow? row)
    {
        if (row is null || _service is null) return;

        Message = "";
        var result = _service.Restore(row.Entry);
        if (!result.Ok)
        {
            Message = result.Error ?? "That backup could not be restored.";
            return;
        }

        if (row.Entry.Kind == BackupKind.Project) _projects?.Refresh();

        Message = row.Entry.Kind == BackupKind.Project
            ? "Project restored — it's back in your Projects list."
            : "Mod restored into SCUM's mods folder.";
        Refresh();
    }

    [RelayCommand]
    private void ConfirmDelete(BackupRow? row)
    {
        if (row is null || _service is null) return;

        var result = _service.Delete(row.Entry);
        Message = result.Ok ? "Backup deleted." : (result.Error ?? "That backup could not be deleted.");
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Message = _folders.Open(_service?.Folder) ? "" : "There's no backups folder yet.";
    }
}
