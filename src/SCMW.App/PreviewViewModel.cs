using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private IReadOnlyList<BuildChange> _lines = Array.Empty<BuildChange>();

    public PreviewViewModel(ItemChangeStore? changes = null, ProjectStore? projects = null)
    {
        _changes = changes;
        _projects = projects;

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

    /// <summary>Re-read the chosen project's changes from disk.</summary>
    public void Refresh()
    {
        var folder = _projects?.Chosen?.FolderPath;
        _lines = folder is not null && _changes is not null
            ? ChangeDiff.Build(_changes.For(folder))
            : Array.Empty<BuildChange>();

        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(NeedsProject));
        OnPropertyChanged(nameof(ProjectName));
    }
}
