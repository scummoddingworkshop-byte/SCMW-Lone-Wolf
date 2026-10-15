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

/// <summary>One kept copy of the player's saved game, as shown on screen.</summary>
public partial class SaveRow : ObservableObject
{
    public SaveRow(SaveSnapshot snapshot) => Snapshot = snapshot;

    public SaveSnapshot Snapshot { get; }

    public string Id => Snapshot.Id;

    /// <summary>Plain "2026-10-13 17:37", or empty when the name carried no stamp.</summary>
    public string When => Snapshot.WhenUtc == DateTime.MinValue
        ? ""
        : Snapshot.WhenUtc.ToString("yyyy-MM-dd HH:mm");

    /// <summary>A short size, e.g. "9.4 MB".</summary>
    public string Size => Snapshot.SizeBytes >= 1024 * 1024
        ? $"{Snapshot.SizeBytes / 1024.0 / 1024.0:0.#} MB"
        : $"{Math.Max(1, Snapshot.SizeBytes / 1024)} KB";

    /// <summary>True while this row's inline delete confirmation is showing.</summary>
    [ObservableProperty] private bool _isConfirmingDelete;
}

/// <summary>
/// Backups' brain: the removed mods and deleted projects SCMW — Lone Wolf keeps in the workspace, with
/// one-click restore and delete. It knows nothing about pixels.
/// </summary>
public partial class BackupsViewModel : ObservableObject
{
    private IBackupsService? _service;
    private ISaveBackupsService? _saves;
    private readonly ProjectStore? _projects;
    private readonly IFolderOpener _folders;

    public BackupsViewModel(IBackupsService? service = null, ProjectStore? projects = null,
                            IFolderOpener? folderOpener = null, ISaveBackupsService? saves = null)
    {
        _service = service;
        _projects = projects;
        _folders = folderOpener ?? new SystemFolderOpener();
        _saves = saves;

        Backups = new ObservableCollection<BackupRow>();
        Saves = new ObservableCollection<SaveRow>();
        Refresh();
        RefreshSaves();
    }

    /// <summary>The backups currently shown, newest first.</summary>
    public ObservableCollection<BackupRow> Backups { get; }

    /// <summary>Deleted projects on their own, newest first — the screen's Projects area.</summary>
    public ObservableCollection<BackupRow> ProjectBackups { get; } = new();

    /// <summary>Replaced mods on their own, newest first — the screen's Mods area.</summary>
    public ObservableCollection<BackupRow> ModBackups { get; } = new();

    /// <summary>A plain line saying what the Projects area is for.</summary>
    public string ProjectNote => "Kept when you delete a project — Restore puts it back in your Projects list.";

    /// <summary>A plain line saying what the Mods area is for.</summary>
    public string ModNote => "Kept when a build replaces a mod already in SCUM — Restore puts the older copy back.";

    public bool HasProjectBackups => ProjectBackups.Count > 0;
    public bool ShowNoProjectBackups => ProjectBackups.Count == 0;
    public bool HasModBackups => ModBackups.Count > 0;
    public bool ShowNoModBackups => ModBackups.Count == 0;

    /// <summary>A plain line for the last thing that happened (a restore, a delete, a refusal).</summary>
    [ObservableProperty] private string _message = "";

    public bool HasBackups => Backups.Count > 0;
    public bool ShowEmptyState => !HasBackups;

    public string Summary => $"{Backups.Count} {(Backups.Count == 1 ? "backup" : "backups")} kept";

    /// <summary>Swaps in a service built from the current settings (e.g. after first-time setup).</summary>
    public void UseService(IBackupsService? service, ISaveBackupsService? saves = null)
    {
        _service = service;
        _saves = saves;
        Message = "";
        Refresh();
        RefreshSaves();
    }

    /// <summary>Re-reads the backups from disk. Called at construction and after any change.</summary>
    public void Refresh()
    {
        Backups.Clear();
        ProjectBackups.Clear();
        ModBackups.Clear();

        if (_service is not null)
        {
            foreach (var entry in _service.List())
            {
                var row = new BackupRow(entry);
                Backups.Add(row);

                // The same row feeds its own area, so both stay in step.
                if (entry.Kind == BackupKind.Project) ProjectBackups.Add(row);
                else ModBackups.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasProjectBackups));
        OnPropertyChanged(nameof(ShowNoProjectBackups));
        OnPropertyChanged(nameof(HasModBackups));
        OnPropertyChanged(nameof(ShowNoModBackups));
        OnPropertyChanged(nameof(Summary));
    }

    // --- The saved game (SCUM.db): back up now, then restore or delete a kept copy ------------------

    /// <summary>Kept copies of the player's saved game, newest first.</summary>
    public ObservableCollection<SaveRow> Saves { get; }

    /// <summary>True when the app knows where SCUM keeps its saves.</summary>
    public bool HasSaveService => _saves is not null;

    /// <summary>True when SCUM's save file was found (the "Back up save now" button can run).</summary>
    public bool CanBackUpSave => _saves?.IsAvailable == true;

    public bool HasSaves => Saves.Count > 0;

    /// <summary>True when we can back up but no copies are kept yet.</summary>
    public bool ShowNoSaves => HasSaveService && Saves.Count == 0;

    /// <summary>A short plain line under the heading.</summary>
    public string SaveNote => !HasSaveService ? ""
        : CanBackUpSave ? "Close SCUM before backing up or restoring."
        : "SCUM's saved game wasn't found yet — play the game once, then try again.";

    /// <summary>Re-reads the kept save copies from disk.</summary>
    public void RefreshSaves()
    {
        Saves.Clear();
        if (_saves is not null)
        {
            foreach (var snapshot in _saves.List()) Saves.Add(new SaveRow(snapshot));
        }

        OnPropertyChanged(nameof(HasSaveService));
        OnPropertyChanged(nameof(CanBackUpSave));
        OnPropertyChanged(nameof(HasSaves));
        OnPropertyChanged(nameof(ShowNoSaves));
        OnPropertyChanged(nameof(SaveNote));
    }

    [RelayCommand]
    private void BackUpSave()
    {
        if (_saves is null) return;

        Message = "";
        var error = _saves.BackUp();
        Message = error ?? "Saved game backed up.";
        RefreshSaves();
    }

    [RelayCommand]
    private void RestoreSave(SaveRow? row)
    {
        if (row is null || _saves is null) return;

        Message = "";
        var error = _saves.Restore(row.Id);
        Message = error ?? "Saved game restored — start SCUM to play from it.";
        RefreshSaves();
    }

    [RelayCommand]
    private void RequestDeleteSave(SaveRow? row)
    {
        if (row is not null) row.IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDeleteSave(SaveRow? row)
    {
        if (row is not null) row.IsConfirmingDelete = false;
    }

    [RelayCommand]
    private void ConfirmDeleteSave(SaveRow? row)
    {
        if (row is null || _saves is null) return;

        var error = _saves.Delete(row.Id);
        Message = error ?? "Save copy deleted.";
        RefreshSaves();
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
