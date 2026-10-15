using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private IReadOnlyList<BuildChange> _lines = Array.Empty<BuildChange>();

    public PreviewViewModel(ItemChangeStore? changes = null, ProjectStore? projects = null,
                            IContentSource? source = null)
    {
        _changes = changes;
        _projects = projects;
        _source = source;

        if (_changes is not null) _changes.Changed += Refresh;
        if (_projects is not null) _projects.Changed += Refresh;
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
        _lines = folder is not null && _changes is not null
            ? ChangeDiff.Build(_changes.For(folder))
            : Array.Empty<BuildChange>();

        foreach (var line in _lines) Enrich(line);

        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(NeedsProject));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectPicture));
        OnPropertyChanged(nameof(HasProjectPicture));
    }

    /// <summary>Give a row its friendly name, its path, and its picture (when the game has one).</summary>
    private void Enrich(BuildChange line)
    {
        line.FriendlyName = ContentCatalog.FriendlyItemName(line.Path);
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
