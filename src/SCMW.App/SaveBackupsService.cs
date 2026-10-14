using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SCMW.App;

/// <summary>One kept copy of the player's saved game.</summary>
public sealed record SaveSnapshot(string Id, DateTime WhenUtc, long SizeBytes, string Path);

/// <summary>
/// Keeps copies of the player's saved game and puts them back. The save is SCUM's own file
/// (<c>SCUM.db</c>), which lives outside the game folder (under the user's LocalAppData); a copy is a
/// plain file kept in the SCMW workspace, and restoring puts one back after keeping the current save
/// first. This is the one soft spot paks leave — a pak never touches the game's files, but a mod can
/// leave a mark in a save — so this is the "just undo it" comfort, for saves.
/// </summary>
public interface ISaveBackupsService
{
    /// <summary>True when SCUM's saved game was found.</summary>
    bool IsAvailable { get; }

    /// <summary>Where SCUM keeps its saves (shown to the player).</summary>
    string SaveFolder { get; }

    /// <summary>Every kept save, newest first. Empty when none exist or the folder is missing.</summary>
    IReadOnlyList<SaveSnapshot> List();

    /// <summary>Keep a copy of the current save now. Null on success, otherwise one plain message.</summary>
    string? BackUp();

    /// <summary>
    /// Put a kept save back over the current one. The current save is kept first, so a restore can
    /// never lose it. Null on success, otherwise one plain message.
    /// </summary>
    string? Restore(string snapshotId);

    /// <summary>Delete a kept save for good. Null on success, otherwise one plain message.</summary>
    string? Delete(string snapshotId);
}

/// <summary>
/// File-backed <see cref="ISaveBackupsService"/>. Copies are <c>&lt;workspace&gt;\Backups\saves\
/// SCUM_yyyyMMdd-HHmmss.db</c>; the game's save is <c>&lt;saveFolder&gt;\SCUM.db</c>. Never throws at
/// the caller: a missing save, a locked file (SCUM still running), or a bad id all come back as a
/// plain message.
/// </summary>
public sealed class SaveBackupsService : ISaveBackupsService
{
    /// <summary>SCUM's save file name.</summary>
    public const string SaveFileName = "SCUM.db";

    /// <summary>Folder under the workspace where kept saves live.</summary>
    public const string SavesFolderName = "saves";

    private const string StemPrefix = "SCUM_";
    private const string StampFormat = "yyyyMMdd-HHmmss";

    private readonly string _saveFolder;
    private readonly string _savesRoot;

    public SaveBackupsService(string? saveFolder, string workspaceRoot)
    {
        _saveFolder = (saveFolder ?? string.Empty).TrimEnd('\\', '/');
        _savesRoot = Path.Combine(workspaceRoot ?? string.Empty, "Backups", SavesFolderName);
    }

    /// <summary>Where SCUM keeps saves on this PC, when it is in the usual place.</summary>
    public static string DefaultSaveFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SCUM", "Saved", "SaveFiles");

    public bool IsAvailable => CurrentSavePath() is not null;

    public string SaveFolder => _saveFolder;

    private string? CurrentSavePath()
    {
        if (_saveFolder.Length == 0) return null;
        var path = Path.Combine(_saveFolder, SaveFileName);
        return File.Exists(path) ? path : null;
    }

    public IReadOnlyList<SaveSnapshot> List()
    {
        if (!Directory.Exists(_savesRoot)) return Array.Empty<SaveSnapshot>();

        var snapshots = new List<SaveSnapshot>();
        try
        {
            foreach (var file in Directory.GetFiles(_savesRoot, "*.db"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                snapshots.Add(new SaveSnapshot(id, WhenFrom(id), new FileInfo(file).Length, file));
            }
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            return Array.Empty<SaveSnapshot>();
        }

        return snapshots
            .OrderByDescending(s => s.WhenUtc)
            .ThenByDescending(s => s.Id, StringComparer.Ordinal)
            .ToList();
    }

    public string? BackUp()
    {
        var current = CurrentSavePath();
        if (current is null)
            return "SCUM's saved game wasn't found. Is SCUM installed and has it been played?";

        try
        {
            Directory.CreateDirectory(_savesRoot);
            File.Copy(current, Path.Combine(_savesRoot, UniqueId(_savesRoot, DateTime.UtcNow) + ".db"));
            return null;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            return "The saved game could not be copied. If SCUM is running, close it and try again.";
        }
    }

    public string? Restore(string snapshotId)
    {
        if (!IsSafeId(snapshotId)) return "That save copy could not be found.";

        var copy = Path.Combine(_savesRoot, snapshotId + ".db");
        if (!File.Exists(copy)) return "That save copy could not be found.";

        var current = CurrentSavePath();
        if (current is null) return "SCUM's saved game wasn't found.";

        try
        {
            // Safety net first: keep the current save, so a restore can never lose it.
            Directory.CreateDirectory(_savesRoot);
            File.Copy(current, Path.Combine(_savesRoot, UniqueId(_savesRoot, DateTime.UtcNow) + ".db"));

            File.Copy(copy, current, overwrite: true);

            // A leftover journal from an unclean exit must not be replayed over the restored file.
            RemoveQuietly(current + "-wal");
            RemoveQuietly(current + "-shm");
            return null;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            return "The saved game could not be restored. If SCUM is running, close it and try again.";
        }
    }

    public string? Delete(string snapshotId)
    {
        if (!IsSafeId(snapshotId)) return "That save copy could not be found.";

        var copy = Path.Combine(_savesRoot, snapshotId + ".db");
        try
        {
            if (File.Exists(copy)) File.Delete(copy);
            return null;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            return "That save copy could not be deleted.";
        }
    }

    /// <summary>A unique snapshot id for <paramref name="utc"/> (a <c>-N</c> suffix avoids a same-second clash).</summary>
    private static string UniqueId(string root, DateTime utc)
    {
        var stem = StemPrefix + utc.ToString(StampFormat, CultureInfo.InvariantCulture);
        var id = stem;
        var suffix = 2;
        while (File.Exists(Path.Combine(root, id + ".db")))
        {
            id = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }
        return id;
    }

    /// <summary>The time a snapshot was kept, read from its own name (with the write time as a fallback).</summary>
    private static DateTime WhenFrom(string id)
    {
        var stem = id.StartsWith(StemPrefix, StringComparison.Ordinal) ? id[StemPrefix.Length..] : id;

        if (DateTime.TryParseExact(stem, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
            return when;

        var dashed = stem.LastIndexOf('-');
        if (dashed > 0
            && DateTime.TryParseExact(stem[..dashed], StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when2))
            return when2;

        return DateTime.MinValue;
    }

    private static void RemoveQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            // Best effort only: a stale journal is unusual; never mask the restore itself.
        }
    }

    /// <summary>A snapshot id may only be a bare file name (no separators or traversal).</summary>
    private static bool IsSafeId(string snapshotId) =>
        !string.IsNullOrWhiteSpace(snapshotId)
        && snapshotId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !snapshotId.Contains("..", StringComparison.Ordinal)
        && string.Equals(Path.GetFileName(snapshotId), snapshotId, StringComparison.Ordinal);

    private static bool IsExpectedIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or NotSupportedException or ArgumentException;
}
