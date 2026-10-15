using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SCMW.Core.Logging;

namespace SCMW.Core.Editing;

/// <summary>
/// The list of change records a single project owns. A project's edits live in
/// <c>&lt;projectFolder&gt;\changes.json</c>; nothing here touches game files.
/// Lists are per-folder, so two projects can never see each other's changes.
/// </summary>
public interface IChangeStore
{
    /// <summary>Every record in the project's list, in the order it was recorded. Empty when none.</summary>
    IReadOnlyList<ChangeRecord> Load(string projectFolder);

    /// <summary>Append a record. Idempotent by <see cref="ChangeRecord.Id"/>: a duplicate is ignored.</summary>
    void Add(string projectFolder, ChangeRecord record);

    /// <summary>Remove the single record with <paramref name="recordId"/>; the rest keep their order.</summary>
    void Undo(string projectFolder, Guid recordId);

    /// <summary>
    /// The applied value for an asset+field: the latest matching record's new value, else
    /// <paramref name="gameValue"/>. Field names match case-insensitively.
    /// </summary>
    string? AppliedValue(string projectFolder, string assetId, string field, string? gameValue);

    /// <summary>
    /// Save the project's current change list as a new version (a snapshot the player can return
    /// to later). Returns the saved version; a failed write is logged and reported plainly to the
    /// caller, which can confirm the version really exists via <see cref="ListVersions"/>.
    /// </summary>
    ProjectVersion SaveVersion(string projectFolder, string? label);

    /// <summary>
    /// Every saved version of the project, newest first. Empty when none exist or the versions
    /// folder is missing/unreadable; a corrupt version file is skipped with a plain warning and
    /// never throws.
    /// </summary>
    IReadOnlyList<ProjectVersion> ListVersions(string projectFolder);

    /// <summary>
    /// Replace the project's change list with a saved version. The current list is saved as an
    /// automatic backup first, so nothing is ever lost. Returns null on success, otherwise one
    /// plain message; an unknown id or a corrupt version file never changes anything and never
    /// throws.
    /// </summary>
    string? Restore(string projectFolder, string versionId);
}

/// <summary>
/// File-backed <see cref="IChangeStore"/>: one camelCase <c>changes.json</c> per project folder,
/// written atomically (a temp file is fully written, then moved over the target) so a failed write
/// can never leave a half-written list. Versions live beside it under
/// <c>&lt;projectFolder&gt;\versions\&lt;yyyyMMdd-HHmmss&gt;.json</c>, each holding the change list
/// captured at save time plus its label. Corrupt or unreadable files are reported with a plain
/// warning and treated as "no changes"/invalid — reads and writes never throw at the caller.
/// </summary>
public sealed class ProjectChangeStore : IChangeStore
{
    /// <summary>File name of the change list inside each project folder.</summary>
    public const string FileName = "changes.json";

    /// <summary>Folder name of the saved versions inside each project folder.</summary>
    public const string VersionsFolderName = "versions";

    /// <summary>Label of the safety snapshot taken automatically before a restore.</summary>
    public const string AutoSnapshotLabel = "Auto-snapshot before restore";

    /// <summary>Version file-name pattern (UTC, second resolution); an <c>-N</c> suffix avoids clashes.</summary>
    public const string VersionIdFormat = "yyyyMMdd-HHmmss";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serialises every read-modify-write on the list, so two edits can't lose one another.</summary>
    private readonly object _gate = new();

    public IReadOnlyList<ChangeRecord> Load(string projectFolder)
    {
        if (IsBlankFolder(projectFolder))
            return Array.Empty<ChangeRecord>();

        lock (_gate)
        {
            var path = Path.Combine(projectFolder, FileName);
            try
            {
                if (!File.Exists(path))
                    return Array.Empty<ChangeRecord>();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return Array.Empty<ChangeRecord>();

                var file = JsonSerializer.Deserialize<ChangesFile>(json, JsonOptions);
                return file?.Records ?? new List<ChangeRecord>();
            }
            catch (JsonException)
            {
                // A damaged file is preserved (never overwritten as if empty) so nothing is lost.
                ArchiveCorrupt(projectFolder, path);
                return Array.Empty<ChangeRecord>();
            }
            catch (Exception ex) when (IsExpectedIoFailure(ex))
            {
                // A hand-edited/corrupt or locked file must never take the app down: report it plainly
                // and behave as if the project has no changes yet.
                Log.Warn($"Could not read the change list at '{path}' - treating the project as having no changes. {ex.Message}");
                return Array.Empty<ChangeRecord>();
            }
        }
    }

    /// <summary>Moves a damaged change list aside (keeping it) so the next save can start clean.</summary>
    private static void ArchiveCorrupt(string projectFolder, string path)
    {
        var kept = Path.Combine(projectFolder,
            $"changes.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        try
        {
            if (File.Exists(path)) File.Move(path, kept, overwrite: true);
            Log.Warn($"The project's change list was damaged; it was kept as '{Path.GetFileName(kept)}' and treated as empty.");
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            Log.Warn($"The project's change list was damaged and could not be kept aside. {ex.Message}");
        }
    }

    public void Add(string projectFolder, ChangeRecord record) => TryAdd(projectFolder, record);

    /// <summary>
    /// Append a record; true when it is (or already was) in the saved list, false when the save
    /// failed. Never throws — the caller can say plainly that the edit wasn't kept.
    /// </summary>
    public bool TryAdd(string projectFolder, ChangeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return false; // no project folder -> nothing to append to (never throws)

            var records = Load(projectFolder).ToList();
            if (records.Any(r => r.Id == record.Id))
                return true; // idempotent by Id

            records.Add(record);
            return TrySave(projectFolder, records);
        }
    }

    public void Undo(string projectFolder, Guid recordId)
    {
        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return; // no project folder -> nothing to remove (never throws)

            var records = Load(projectFolder).ToList();
            var kept = records.Where(r => r.Id != recordId).ToList();
            if (kept.Count == records.Count)
                return; // nothing matched: leave the file untouched

            TrySave(projectFolder, kept);
        }
    }

    /// <summary>
    /// Replace the record with the same <see cref="ChangeRecord.Id"/> in place (keeping its position),
    /// or append it when absent. True when it is saved; false on a failed write (the previous list is
    /// left unchanged). Never throws.
    /// </summary>
    public bool Replace(string projectFolder, ChangeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return false;

            var records = Load(projectFolder).ToList();
            var at = records.FindIndex(r => r.Id == record.Id);
            if (at >= 0)
                records[at] = record;
            else
                records.Add(record);

            return TrySave(projectFolder, records);
        }
    }

    public string? AppliedValue(string projectFolder, string assetId, string field, string? gameValue) =>
        SCMW.Core.Editing.AppliedValue.Resolve(Load(projectFolder), assetId, field, gameValue);

    public ProjectVersion SaveVersion(string projectFolder, string? label)
    {
        var labelText = (label ?? string.Empty).Trim();

        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
            {
                // No project folder -> no version can be written. Return a version-shaped no-op the
                // caller cannot confirm against ListVersions (which is also empty), so the UI reports
                // the plain failure instead of an unhandled exception.
                Log.Warn("Cannot save a version: no project folder was supplied.");
                return new ProjectVersion(string.Empty, labelText, DateTime.UtcNow, 0);
            }

            var records = Load(projectFolder).ToList();
            var createdUtc = DateTime.UtcNow;
            var id = UniqueVersionId(projectFolder, createdUtc);

            if (!TryWriteVersionFile(projectFolder, id,
                    new VersionFile(labelText, createdUtc, records)))
            {
                Log.Warn($"Could not save a version for the project at '{projectFolder}'.");
            }

            return new ProjectVersion(id, labelText, createdUtc, records.Count);
        }
    }

    public IReadOnlyList<ProjectVersion> ListVersions(string projectFolder)
    {
        if (IsBlankFolder(projectFolder))
            return Array.Empty<ProjectVersion>();

        var dir = VersionsDir(projectFolder);
        try
        {
            if (!Directory.Exists(dir))
                return Array.Empty<ProjectVersion>();

            var versions = new List<ProjectVersion>();
            foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
            {
                var version = TryReadVersion(path);
                if (version is not null)
                    versions.Add(version);
            }

            return versions
                .OrderByDescending(v => v.CreatedUtc)
                .ThenByDescending(v => v.Id, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            Log.Warn($"Could not list the project's versions at '{dir}' - treating it as having none. {ex.Message}");
            return Array.Empty<ProjectVersion>();
        }
    }

    public string? Restore(string projectFolder, string versionId)
    {
        lock (_gate)
        {
            return RestoreCore(projectFolder, versionId);
        }
    }

    private string? RestoreCore(string projectFolder, string versionId)
    {
        if (IsBlankFolder(projectFolder))
            return "That project folder is unavailable.";

        if (!IsSafeVersionId(versionId))
            return "That version could not be found.";

        var path = Path.Combine(VersionsDir(projectFolder), versionId + ".json");
        VersionFile? version;
        try
        {
            if (!File.Exists(path))
                return "That version could not be found.";

            var json = File.ReadAllText(path);
            version = JsonSerializer.Deserialize<VersionFile>(json, JsonOptions);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            Log.Warn($"Could not read the project version at '{path}'. {ex.Message}");
            return "That version file could not be read.";
        }

        if (version is null)
            return "That version file could not be read.";

        // A valid JSON file with no `records` field at all is corrupt: refuse it plainly and change
        // nothing (no auto-snapshot either). An explicit empty list is a legitimate empty snapshot
        // (the app itself writes one when a version is saved before any change), so it restores.
        var records = version.Records;
        if (records is null)
        {
            Log.Warn($"The project version at '{path}' has no change records field - refusing to restore it.");
            return "That version file is empty or damaged, so nothing was restored.";
        }

        // Safety net first: capture the current list so a restore can never lose an edit.
        var current = Load(projectFolder).ToList();
        var snapshotAt = DateTime.UtcNow;
        var snapshotId = UniqueVersionId(projectFolder, snapshotAt);
        if (!TryWriteVersionFile(projectFolder, snapshotId,
                new VersionFile(AutoSnapshotLabel, snapshotAt, current)))
        {
            return "SCMW could not save an automatic backup before restoring, so nothing was changed.";
        }

        if (!TrySave(projectFolder, records))
            return "That version could not be restored - your current changes are unchanged.";

        return null;
    }

    /// <summary>
    /// True when <paramref name="projectFolder"/> is null or blank. Every path this store builds
    /// needs a real folder: a null one would make <see cref="Path.Combine(string?,string?)"/> throw
    /// an <see cref="ArgumentNullException"/>, while a blank one would resolve to a stray relative
    /// path. Rejecting it up front is what keeps the store's never-throws contract (see the class
    /// remarks); callers only ever pass a resolved project folder.
    /// </summary>
    private static bool IsBlankFolder(string? projectFolder) =>
        string.IsNullOrWhiteSpace(projectFolder);

    /// <summary>Folder holding the project's saved versions.</summary>
    private static string VersionsDir(string projectFolder) =>
        Path.Combine(projectFolder, VersionsFolderName);

    /// <summary>
    /// A unique version id for <paramref name="createdUtc"/>: the timestamp stem, plus an
    /// <c>-N</c> suffix if two versions are saved within the same second.
    /// </summary>
    private static string UniqueVersionId(string projectFolder, DateTime createdUtc)
    {
        var stem = createdUtc.ToString(VersionIdFormat, CultureInfo.InvariantCulture);
        var dir = VersionsDir(projectFolder);
        var id = stem;
        var suffix = 2;
        while (File.Exists(Path.Combine(dir, id + ".json")))
        {
            id = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return id;
    }

    /// <summary>A version id may only be a bare file name (no separators or traversal).</summary>
    private static bool IsSafeVersionId(string versionId) =>
        !string.IsNullOrWhiteSpace(versionId)
        && versionId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !versionId.Contains("..", StringComparison.Ordinal)
        && string.Equals(Path.GetFileName(versionId), versionId, StringComparison.Ordinal);

    private static ProjectVersion? TryReadVersion(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var file = JsonSerializer.Deserialize<VersionFile>(json, JsonOptions);
            if (file is null)
                return null;

            var id = Path.GetFileNameWithoutExtension(path);
            var created = file.CreatedUtc == default ? FallbackCreatedUtc(path, id) : file.CreatedUtc;
            return new ProjectVersion(id, file.Label ?? string.Empty, created, file.Records?.Count ?? 0);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            Log.Warn($"Could not read the project version at '{path}' - skipping it. {ex.Message}");
            return null;
        }
    }

    /// <summary>Best-effort timestamp for a hand-made version file that carries none.</summary>
    private static DateTime FallbackCreatedUtc(string path, string id)
    {
        if (id.Length >= 15
            && DateTime.TryParseExact(id[..15], VersionIdFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Write the whole change list atomically: serialise to a temp file in the same folder, then
    /// move it over <c>changes.json</c>. If anything fails the temp file is removed, the previous
    /// <c>changes.json</c> is left exactly as it was, and false is returned.
    /// </summary>
    private static bool TrySave(string projectFolder, List<ChangeRecord> records)
    {
        var path = Path.Combine(projectFolder, FileName);
        var temp = Path.Combine(projectFolder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(projectFolder);
            var json = JsonSerializer.Serialize(new ChangesFile(records), JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            TryDeleteFile(temp);
            Log.Warn($"Could not save the change list at '{path}' - the previous list is unchanged. {ex.Message}");
            return false;
        }
    }

    /// <summary>Write one version file atomically; false when the write failed (temp removed).</summary>
    private static bool TryWriteVersionFile(string projectFolder, string id, VersionFile file)
    {
        var dir = VersionsDir(projectFolder);
        var path = Path.Combine(dir, id + ".json");
        var temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(file, JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            TryDeleteFile(temp);
            Log.Warn($"Could not save the project version at '{path}'. {ex.Message}");
            return false;
        }
    }

    private static bool IsExpectedIoFailure(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException or System.Security.SecurityException
            or NotSupportedException or ArgumentException;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            // Best effort only: never mask the original failure.
        }
    }

    /// <summary>On-disk shape of the change list; wrapped so future fields can be added without a breaking change.</summary>
    private sealed record ChangesFile(List<ChangeRecord>? Records);

    /// <summary>On-disk shape of a saved version: the label plus the change list captured at save time.</summary>
    private sealed record VersionFile(string? Label, DateTime CreatedUtc, List<ChangeRecord>? Records);
}
