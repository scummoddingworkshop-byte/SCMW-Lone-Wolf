using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Core.Content;

namespace SCMW.App;

/// <summary>
/// The preview/diff a player sees <em>before</em> building (design §10): exactly which files the
/// chosen project will change, and each field's value before → after. It reads the same records the
/// build does, so what the preview shows is what will be built.
/// </summary>
public partial class PreviewViewModel : ObservableObject
{
    private readonly ItemChangeStore? _changes;
    private readonly ProjectStore? _projects;
    private readonly IContentSource? _source;
    private readonly SCMW.Core.Editing.INewAssetStore? _newAssets;
    private IReadOnlyList<BuildChange> _lines = Array.Empty<BuildChange>();

    public PreviewViewModel(ItemChangeStore? changes = null, ProjectStore? projects = null,
                            IContentSource? source = null,
                            SCMW.Core.Editing.INewAssetStore? newAssets = null)
    {        _changes = changes;
        _projects = projects;
        _source = source;
        _newAssets = newAssets;

        if (_changes is not null) _changes.Changed += Refresh;
        if (_projects is not null) _projects.Changed += Refresh;
        if (_newAssets is not null) _newAssets.Changed += Refresh;
        Refresh();
    }

    /// <summary>The diff: one row per changed file, in plain "before → after" lines.</summary>
    public IReadOnlyList<BuildChange> Changes => _lines;

    /// <summary>True when the chosen project has at least one change to build.</summary>
    public bool HasChanges => _lines.Count > 0;

    /// <summary>True when a store is wired but the player has not chosen a project yet.</summary>
    public bool NeedsProject => _changes is not null && !(_projects?.HasChosenProject ?? false);

    /// <summary>Plain words shown when there is nothing to preview yet.</summary>
    public string NoChangesNote => "Nothing to show yet — changes you make appear here before you build.";

    /// <summary>Plain words shown when no project is chosen.</summary>
    public string ProjectWarning =>
        "Choose a project first — its changes are what this preview shows.";

    /// <summary>The chosen project's name, so the preview says which mod it is about.</summary>
    public string ProjectName => _projects?.Chosen?.Name ?? "No project open";

    /// <summary>The chosen project's picture, if it has one.</summary>
    public IImage? ProjectPicture => ProjectSummary.LoadPicture(_projects?.Chosen?.IconPath);

    /// <summary>True when the chosen project has a picture to show.</summary>
    public bool HasProjectPicture => ProjectPicture is not null;

    /// <summary>Re-read the chosen project's changes from disk.</summary>
    public void Refresh()
    {
        var folder = _projects?.Chosen?.FolderPath;
        var lines = folder is not null && _changes is not null
            ? ChangeDiff.Build(_changes.For(folder)).ToList()
            : new List<BuildChange>();

        // A brand-new asset has no game file to look up, so its edits arrive as ordinary records aimed
        // at its package path; an asset with no edits yet has no records at all. Either way it is part
        // of the mod, so it belongs in the list.
        if (folder is not null && _newAssets is not null)
        {
            var byPath = lines.ToDictionary(l => l.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var asset in _newAssets.Load(folder))
            {
                var key = ChangeDiff.ItemKey(asset.EditPath);
                if (byPath.TryGetValue(key, out var existing))
                {
                    existing.IsNewAsset = true;
                    existing.NewAssetName = asset.Name;
                }
                else
                {
                    var row = new BuildChange(asset.Name, Array.Empty<BuildValueChange>(), key)
                    {
                        IsNewAsset = true,
                        NewAssetName = asset.Name,
                    };
                    lines.Insert(0, row);
                    byPath[key] = row;
                }
            }
        }

        _lines = lines;

        foreach (var line in _lines) Enrich(line);

        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(NeedsProject));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectPicture));
        OnPropertyChanged(nameof(HasProjectPicture));

        // Item pictures are a fast follow: the list is usable without them.
        _ = ExtractIconsAsync();
    }

    /// <summary>Removes every change for one item, so it reverts to the game's own values.</summary>
    [RelayCommand]
    private void RemoveItem(BuildChange? row)
    {
        var folder = _projects?.Chosen?.FolderPath;
        if (row is null || folder is null || _changes is null) return;

        _changes.RemoveItem(folder, row.Path);   // raises Changed → Refresh
    }

    /// <summary>Raised when the player clicks a card: the shell then opens that item for editing.</summary>
    public event Action<string>? OpenItemRequested;

    /// <summary>One card was clicked: ask the shell to open that item in the Modify screen.</summary>
    [RelayCommand]
    private void OpenItem(BuildChange? row)
    {
        if (row is null || string.IsNullOrEmpty(row.Path)) return;

        OpenItemRequested?.Invoke(row.Path);
    }

    /// <summary>Extract the changed items' icons into the cache, then point each row at its picture.</summary>
    public async System.Threading.Tasks.Task ExtractIconsAsync()
    {
        if (_source is null) return;

        try
        {
            var ids = _lines.Where(l => !l.IsNewAsset)
                            .Select(l => l.Path)
                            .Where(p => !string.IsNullOrEmpty(p))
                            .ToList();
            if (ids.Count > 0)
                await _source.ExtractIconsAsync(ids).ConfigureAwait(false);
        }
        catch
        {
            // Icons are optional — a failure simply leaves the fallback mark.
        }

        foreach (var line in _lines)
        {
            var picture = LoadIcon(line.Path);
            RunOnUi(() => line.Picture = picture);
        }
    }

    /// <summary>Runs an action on the UI thread (the icon pass awaits off-thread).</summary>
    private static void RunOnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>Give a row its friendly name, its path, and its picture (when the game has one).</summary>
    private void Enrich(BuildChange line)
    {
        // A new asset shows its own short name; a game file shows its name made friendly.
        line.FriendlyName = line.NewAssetName ?? ContentCatalog.FriendlyItemName(line.Path);
        line.Picture = LoadIcon(line.Path);
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
            return null;   // no icon is fine — the row shows its fallback mark
        }
    }
}
