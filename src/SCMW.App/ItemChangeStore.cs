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
    /// Append one edit to the project's <c>changes.json</c>. Returns false when it could not be
    /// saved (so the screen can say so), true otherwise. A no-op when no project matches
    /// <paramref name="projectId"/> (never throws).
    /// </summary>
    public bool RecordEdit(string assetId, string field, string? oldValue, string newValue, string projectId,
                           string? assetPath = null)
    {
        var folder = FolderFor(projectId);
        if (folder is null) return false;

        var saved = _store.TryAdd(folder, new EngineChangeRecord(
            Guid.NewGuid(), projectId, assetId, field, oldValue, newValue,
            DateTime.UtcNow, StatusKind.Editable, AssetPath: assetPath));

        Changed?.Invoke();
        return saved;
    }

    /// <summary>
    /// Change the value of an "add a field" record in place (an added field's value lives in that
    /// record, so editing it must update the record, never add a separate Set). Returns true when it
    /// was saved; false when the record is gone or the save failed (never throws).
    /// </summary>
    public bool UpdateAddField(string projectFolder, Guid recordId, string newValue)
    {
        if (string.IsNullOrWhiteSpace(projectFolder)) return false;

        var record = _store.Load(projectFolder).FirstOrDefault(r => r.Id == recordId);
        if (record is null) return false;

        var saved = _store.Replace(projectFolder, record with { NewValue = newValue });
        Changed?.Invoke();
        return saved;
    }

    /// <summary>
    /// Append a multiply (scale by a factor) to the project's <c>changes.json</c>. The factor is kept
    /// as the record's new value; the factor always scales the file's own value, so the newest change
    /// to the field wins. Returns false when it could not be saved (so the screen can say so).
    /// </summary>
    public bool RecordMultiply(string assetId, string field, string? oldValue, string factor, string projectId,
                               string? assetPath = null)
    {
        var folder = FolderFor(projectId);
        if (folder is null) return false;

        var saved = _store.TryAdd(folder, new EngineChangeRecord(
            Guid.NewGuid(), projectId, assetId, field, oldValue, factor,
            DateTime.UtcNow, StatusKind.Editable, Operation: ChangeOperation.Multiply, AssetPath: assetPath));

        Changed?.Invoke();
        return saved;
    }

    /// <summary>
    /// Append an "add a field" change to the project's <c>changes.json</c>. The record keeps the new
    /// field's value and the donor file whose exact shape is copied at build time. Returns the new
    /// record's id (so the field can be removed again), or null when it could not be saved.
    /// </summary>
    public Guid? RecordAddField(string assetId, string field, string newValue, string donorPath, string projectId,
                                string? assetPath = null)
    {
        var folder = FolderFor(projectId);
        if (folder is null) return null;

        var id = Guid.NewGuid();
        var saved = _store.TryAdd(folder, new EngineChangeRecord(
            id, projectId, assetId, field, null, newValue,
            DateTime.UtcNow, StatusKind.Editable, Operation: ChangeOperation.AddField,
            AssetPath: assetPath, DonorPath: donorPath));

        Changed?.Invoke();
        return saved ? id : null;
    }

    /// <summary>
    /// Append a list change (add or remove an entry) to the project's <c>changes.json</c>. Kept as
    /// its own record so the order of several list changes survives (a value edit does not).
    /// <para>
    /// Removing an entry shifts the later slots, so the recipe's existing value edits are dropped
    /// first: it is better to lose them (and re-do them) than to write a number onto the wrong
    /// ingredient, which would build successfully and silently. Returns how many edits were dropped
    /// so the screen can say so.
    /// </para>
    /// </summary>
    public int RecordListChange(string assetId, string field, ChangeOperation operation, string projectId,
                                string? assetPath = null)
    {
        var folder = FolderFor(projectId);
        if (folder is null) return 0;

        var dropped = 0;
        if (operation == ChangeOperation.RemoveEntry)
        {
            var staleEdits = _store.Load(folder)
                .Where(r => r.Operation == ChangeOperation.Set
                            && string.Equals(r.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Id)
                .ToList();

            dropped = staleEdits.Count;
            foreach (var id in staleEdits) _store.Undo(folder, id);
        }

        _store.TryAdd(folder, new EngineChangeRecord(
            Guid.NewGuid(), projectId, assetId, field, null, "",
            DateTime.UtcNow, StatusKind.Editable, Operation: operation, AssetPath: assetPath));

        Changed?.Invoke();
        return dropped;
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
    /// Remove every recorded change for one item — matched by the file's path when the row carries
    /// one, otherwise by the recorded name — so it reverts to the game's own values. Returns null on
    /// success, otherwise one plain message.
    /// </summary>
    public string? RemoveItem(string projectFolder, string assetName)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return "That project folder is unavailable.";

        var ids = _store.Load(projectFolder)
            .Where(r => string.Equals(r.AssetId, assetName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.AssetPath, assetName, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .ToList();

        foreach (var id in ids) _store.Undo(projectFolder, id);
        if (ids.Count > 0) Changed?.Invoke();
        return null;
    }

    private string? FolderFor(string projectId) =>
        _projects.All().FirstOrDefault(p => p.Id == projectId)?.FolderPath;
}
