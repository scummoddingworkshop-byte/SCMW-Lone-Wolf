using System;
using System.Collections.Generic;
using System.Linq;
using SCMW.Core;
using SCMW.Core.Editing;
using EngineChangeRecord = SCMW.Core.Editing.ChangeRecord;

namespace SCMW.App;

/// <summary>
/// The one place the app talks to the engine's project-change machinery. It turns the current
/// project into a folder, appends one record per edit to that project's <c>changes.json</c>, and
/// reports the values those records imply. Nothing here writes to SCUM's files — records only.
/// </summary>
public sealed class ItemChangeStore
{
    private readonly ProjectChangeStore _store;
    private readonly ProjectStore _projects;

    public ItemChangeStore(ProjectChangeStore store, ProjectStore projects)
    {
        _store = store;
        _projects = projects;
    }

    /// <summary>Raised after a record is added or removed. Screens re-read rather than caching.</summary>
    public event Action? Changed;

    /// <summary>The project's records, in the order they were recorded. Empty when none.</summary>
    public IReadOnlyList<EngineChangeRecord> For(string projectFolder) => _store.Load(projectFolder);

    /// <summary>
    /// The value a field should currently show for the project: the newest matching record's new
    /// value, else <paramref name="gameValue"/>.
    /// </summary>
    public string? AppliedValue(string projectFolder, string assetId, string field, string? gameValue) =>
        _store.AppliedValue(projectFolder, assetId, field, gameValue);

    /// <summary>
    /// Append one edit to the project's <c>changes.json</c>. A no-op when no project matches
    /// <paramref name="projectId"/> (never throws).
    /// </summary>
    public void RecordEdit(string assetId, string field, string? oldValue, string newValue, string projectId)
    {
        var folder = FolderFor(projectId);
        if (folder is null) return;

        _store.Add(folder, new EngineChangeRecord(
            Guid.NewGuid(), projectId, assetId, field, oldValue, newValue,
            DateTime.UtcNow, StatusKind.Editable));

        Changed?.Invoke();
    }

    /// <summary>
    /// Remove the single record with <paramref name="id"/> from the project's list. Returns null
    /// on success, otherwise one plain message.
    /// </summary>
    public string? Undo(string projectFolder, Guid id)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return "That project folder is unavailable.";

        _store.Undo(projectFolder, id);
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Remove every recorded change for one item (matched by the recorded name), so it reverts to
    /// the game's own values. Returns null on success, otherwise one plain message.
    /// </summary>
    public string? RemoveItem(string projectFolder, string assetName)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return "That project folder is unavailable.";

        var ids = _store.Load(projectFolder)
            .Where(r => string.Equals(r.AssetId, assetName, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .ToList();

        foreach (var id in ids) _store.Undo(projectFolder, id);
        if (ids.Count > 0) Changed?.Invoke();
        return null;
    }

    private string? FolderFor(string projectId) =>
        _projects.All().FirstOrDefault(p => p.Id == projectId)?.FolderPath;
}
